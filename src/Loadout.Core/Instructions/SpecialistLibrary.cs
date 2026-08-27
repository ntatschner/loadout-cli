using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Loadout.Models.Instructions;
using Loadout.Models.Results;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Loadout.Core.Instructions;

/// <summary>Everything the launcher knows how to say, and where each part came from.</summary>
/// <param name="Specialists">Every specialist, by id.</param>
/// <param name="Findings">Anything wrong with the library as loaded.</param>
public sealed record SpecialistCatalogue(
    IReadOnlyDictionary<string, SpecialistDocument> Specialists,
    IReadOnlyList<RuleFinding> Findings)
{
    public static readonly SpecialistCatalogue Empty =
        new(new Dictionary<string, SpecialistDocument>(StringComparer.OrdinalIgnoreCase), []);

    public IEnumerable<SpecialistDocument> All => Specialists.Values;

    public IEnumerable<SpecialistDocument> OfKind(SpecialistKind kind) =>
        Specialists.Values.Where(s => s.Kind == kind);

    public SpecialistDocument? Find(string id) =>
        Specialists.TryGetValue(id, out var found) ? found : null;

    /// <summary>Whether anything found makes the library unsafe to rely on.</summary>
    public bool HasErrors => Findings.Any(f => f.Severity == RuleFindingSeverity.Error);
}

/// <summary>Loads the specialist library.</summary>
public interface ISpecialistLibrary
{
    /// <summary>
    /// Loads the built-in specialists, then the workspace's, then the project's,
    /// each layer overriding the last by id.
    /// </summary>
    /// <param name="workspaceRoot">Workspace clone, or null to load built-ins alone.</param>
    /// <param name="slug">Project whose specialists to layer on, or null for none.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<SpecialistCatalogue> LoadAsync(
        string? workspaceRoot,
        string? slug = null,
        CancellationToken ct = default);
}

/// <summary>
/// Reads specialists from the launcher itself and from the workspace.
/// </summary>
/// <remarks>
/// <para>
/// There is no registry file. A specialist is one markdown file that describes
/// itself in its own frontmatter, exactly as a path-scoped rule already does.
/// The alternative — a manifest listing paths and activation separately — makes
/// three failures possible that this shape cannot have: the manifest naming a
/// file that is not there, the two drifting apart as one is edited without the
/// other, and a path in the manifest pointing outside the library.
/// </para>
/// <para>
/// Layered the way rules are: built-in, then workspace, then project, with the
/// narrower source winning. That is what lets somebody disagree with a built-in
/// specialist without editing the launcher, and what stops a specialist library
/// being a thing only its author can change.
/// </para>
/// </remarks>
public sealed partial class SpecialistLibrary : ISpecialistLibrary
{
    /// <summary>
    /// Refuses anything larger than this.
    /// </summary>
    /// <remarks>
    /// A specialist is guidance somebody composed, not a manual. The compiler
    /// already refuses oversized context sources for the same reason; this is
    /// smaller because a specialist that needs sixty-four kilobytes to say what
    /// it cares about has stopped being composable.
    /// </remarks>
    private const long LargestSpecialist = 64 * 1024;

    private const string BuiltInPrefix = "Loadout.Core.Specialists.";

    private static readonly IDeserializer Frontmatter = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <inheritdoc />
    public async Task<SpecialistCatalogue> LoadAsync(
        string? workspaceRoot,
        string? slug = null,
        CancellationToken ct = default)
    {
        var specialists = new Dictionary<string, SpecialistDocument>(StringComparer.OrdinalIgnoreCase);
        var findings = new List<RuleFinding>();

        LoadBuiltIn(specialists, findings);

        if (workspaceRoot is { Length: > 0 })
        {
            await LoadDirectoryAsync(
                Path.Combine(workspaceRoot, "global", "specialists"),
                SpecialistOrigin.Workspace,
                specialists,
                findings,
                ct).ConfigureAwait(false);

            if (slug is { Length: > 0 })
            {
                await LoadDirectoryAsync(
                    Path.Combine(workspaceRoot, "projects", slug, "specialists"),
                    SpecialistOrigin.Project,
                    specialists,
                    findings,
                    ct).ConfigureAwait(false);
            }
        }

        findings.AddRange(SpecialistValidator.Validate(specialists));

        return new SpecialistCatalogue(specialists, findings);
    }

    /// <summary>
    /// Loads the specialists shipped inside the launcher.
    /// </summary>
    /// <remarks>
    /// Embedded rather than installed as files. They cannot then be edited in
    /// place, cannot go missing from an install, and cannot be made to point
    /// anywhere — three whole classes of problem that simply do not arise for
    /// content that is not on a disk. Somebody who wants to change one puts a
    /// specialist of the same id in their workspace, which is the supported way
    /// to disagree with a default.
    /// </remarks>
    private static void LoadBuiltIn(
        Dictionary<string, SpecialistDocument> specialists,
        List<RuleFinding> findings)
    {
        var assembly = typeof(SpecialistLibrary).Assembly;

        foreach (var resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(BuiltInPrefix, StringComparison.Ordinal)
                || !resource.EndsWith(".md", StringComparison.Ordinal))
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(resource);

            if (stream is null)
            {
                continue;
            }

            using var reader = new StreamReader(stream, Encoding.UTF8);

            var text = reader.ReadToEnd();

            Absorb(text, resource, SpecialistOrigin.BuiltIn, specialists, findings);
        }
    }

    /// <summary>Loads every specialist under one directory.</summary>
    private static async Task LoadDirectoryAsync(
        string directory,
        SpecialistOrigin origin,
        Dictionary<string, SpecialistDocument> specialists,
        List<RuleFinding> findings,
        CancellationToken ct)
    {
        if (!Directory.Exists(directory))
        {
            // A workspace with no specialists of its own is the ordinary case.
            return;
        }

        string root;

        try
        {
            root = Path.GetFullPath(directory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            findings.Add(new RuleFinding(
                null, RuleFindingSeverity.Error, "specialist-root",
                $"The specialist directory '{directory}' is not a usable path."));

            return;
        }

        foreach (var file in Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();

            if (!IsInside(root, file, out var reason))
            {
                // A link pointing out of the library is not a specialist this
                // will read. Instructions are part of the agent's trust
                // boundary, and the boundary has to be a real one.
                findings.Add(new RuleFinding(
                    Path.GetFileNameWithoutExtension(file),
                    RuleFindingSeverity.Error,
                    "specialist-escape",
                    reason));

                continue;
            }

            var length = new FileInfo(file).Length;

            if (length > LargestSpecialist)
            {
                findings.Add(new RuleFinding(
                    Path.GetFileNameWithoutExtension(file),
                    RuleFindingSeverity.Warning,
                    "specialist-too-large",
                    $"'{Path.GetFileName(file)}' is {length / 1024}KB, over the "
                    + $"{LargestSpecialist / 1024}KB limit, and was not loaded."));

                continue;
            }

            string text;

            try
            {
                text = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                findings.Add(new RuleFinding(
                    Path.GetFileNameWithoutExtension(file),
                    RuleFindingSeverity.Warning,
                    "specialist-unreadable",
                    $"Could not read '{Path.GetFileName(file)}': {ex.Message}"));

                continue;
            }

            Absorb(text, file, origin, specialists, findings);
        }
    }

    /// <summary>
    /// Whether a file really sits under the root it was found through.
    /// </summary>
    /// <remarks>
    /// Enumeration follows directory links, so a link inside the library
    /// pointing at somebody's home directory would otherwise be read as though
    /// it were a specialist. The link target is resolved and checked rather
    /// than the path as written, because the path as written always looks
    /// contained.
    /// </remarks>
    private static bool IsInside(string root, string candidate, out string reason)
    {
        reason = string.Empty;

        try
        {
            var full = Path.GetFullPath(candidate);

            // A link is followed to whatever it really is before being judged.
            var info = new FileInfo(full);
            var target = info.ResolveLinkTarget(returnFinalTarget: true);

            if (target is not null)
            {
                full = Path.GetFullPath(target.FullName);
            }

            var boundary = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            if (full.StartsWith(boundary, comparison))
            {
                return true;
            }

            reason = $"'{Path.GetFileName(candidate)}' resolves outside the specialist "
                + "directory and was not loaded.";

            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or PathTooLongException)
        {
            reason = $"'{Path.GetFileName(candidate)}' could not be resolved and was not loaded.";

            return false;
        }
    }

    /// <summary>Parses one file and adds it, replacing any earlier specialist of the same id.</summary>
    private static void Absorb(
        string text,
        string path,
        SpecialistOrigin origin,
        Dictionary<string, SpecialistDocument> specialists,
        List<RuleFinding> findings)
    {
        var parsed = Parse(text, path, origin);

        if (parsed.Failed)
        {
            findings.Add(new RuleFinding(
                Path.GetFileNameWithoutExtension(path),
                RuleFindingSeverity.Error,
                "specialist-invalid",
                parsed.Error!));

            return;
        }

        var document = parsed.Value!;

        // A later layer replacing an earlier one is the point of layering, so
        // it is not a finding. Two files in the same layer claiming one id is,
        // because then which one wins depends on the order the filesystem
        // happened to return them.
        if (specialists.TryGetValue(document.Id, out var existing) && existing.Origin == origin)
        {
            findings.Add(new RuleFinding(
                document.Id,
                RuleFindingSeverity.Error,
                "specialist-duplicate",
                $"Two {origin} specialists both claim the id '{document.Id}'."));

            return;
        }

        specialists[document.Id] = document;
    }

    /// <summary>
    /// Reads one specialist file.
    /// </summary>
    /// <remarks>
    /// Frontmatter is required, unlike a rule's, where it is optional. A rule
    /// without frontmatter is still a usable instruction with an undeclared
    /// scope; a specialist without frontmatter has no id, no kind and nothing
    /// that could ever activate it, so it is not a specialist at all.
    /// </remarks>
    internal static OperationResult<SpecialistDocument> Parse(
        string text,
        string path,
        SpecialistOrigin origin)
    {
        var match = FrontmatterBlock().Match(text);

        if (!match.Success)
        {
            return OperationResult<SpecialistDocument>.Fail(
                $"'{Path.GetFileName(path)}' has no frontmatter, so it declares no id or kind.");
        }

        SpecialistFront? front;

        try
        {
            front = Frontmatter.Deserialize<SpecialistFront>(match.Groups["front"].Value);
        }
        catch (YamlException ex)
        {
            return OperationResult<SpecialistDocument>.Fail(
                $"'{Path.GetFileName(path)}' has frontmatter that could not be read: {ex.Message}");
        }

        if (front is null || string.IsNullOrWhiteSpace(front.Id))
        {
            return OperationResult<SpecialistDocument>.Fail(
                $"'{Path.GetFileName(path)}' declares no id.");
        }

        if (!Enum.TryParse<SpecialistKind>(front.Kind, ignoreCase: true, out var kind))
        {
            var known = string.Join(", ", Enum.GetNames<SpecialistKind>().Select(n => n.ToLowerInvariant()));

            return OperationResult<SpecialistDocument>.Fail(
                $"'{front.Id}' declares kind '{front.Kind}', which is not one of: {known}.");
        }

        var body = text[match.Length..].Trim('\r', '\n');

        if (string.IsNullOrWhiteSpace(body))
        {
            return OperationResult<SpecialistDocument>.Fail(
                $"'{front.Id}' has no guidance in it.");
        }

        var activation = new SpecialistActivation(
            front.Always,
            front.Globs,
            front.Dependencies,
            front.TaskPhrases,
            front.Requires,
            front.Capabilities,
            front.Modes);

        return OperationResult<SpecialistDocument>.Ok(new SpecialistDocument(
            front.Id.Trim(),
            kind,
            string.IsNullOrWhiteSpace(front.Title) ? front.Id : front.Title.Trim(),
            front.Summary?.Trim() ?? string.Empty,
            activation,
            body,
            Encoding.UTF8.GetByteCount(body),
            origin,
            origin == SpecialistOrigin.BuiltIn ? string.Empty : path));
    }

    /// <summary>The frontmatter as written, before it is checked.</summary>
    /// <remarks>
    /// A mutable class with settable properties because that is what the YAML
    /// deserialiser requires. It is not used as a value anywhere past parsing.
    /// </remarks>
    internal sealed class SpecialistFront
    {
        public string Id { get; set; } = string.Empty;

        public string Kind { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string Summary { get; set; } = string.Empty;

        public bool Always { get; set; }

        public List<string> Globs { get; set; } = [];

        public List<string> Dependencies { get; set; } = [];

        public List<string> TaskPhrases { get; set; } = [];

        public List<string> Requires { get; set; } = [];

        public List<string> Capabilities { get; set; } = [];

        public List<string> Modes { get; set; } = [];
    }

    [GeneratedRegex(@"\A---\r?\n(?<front>.*?)\r?\n---[ \t]*\r?\n",
        RegexOptions.Singleline, 1000)]
    private static partial Regex FrontmatterBlock();
}
