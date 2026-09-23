using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using Loadout.Models.Instructions;
using Loadout.Models.Teams;

namespace Loadout.Core.Teams;

/// <summary>What was found, and anything wrong with it.</summary>
/// <param name="Capabilities">By id, nearest layer winning.</param>
/// <param name="Findings">Anything a person should change.</param>
/// <param name="Origins">Which layer each one came from.</param>
public sealed record CapabilityCatalogueResult(
    IReadOnlyDictionary<string, DeclarationCapability> Capabilities,
    IReadOnlyList<RuleFinding> Findings,
    IReadOnlyDictionary<string, SpecialistOrigin> Origins);

/// <summary>
/// The capabilities a declaration can ask for, from every layer.
/// </summary>
/// <remarks>
/// Built-in, then packs, then the workspace, then the project, each replacing
/// the one before by id — the same order teams and specialists already follow,
/// so a pack can ship one and a project can replace it, and never the other
/// way round.
/// </remarks>
public sealed class CapabilityCatalogue
{
    private const string BuiltInPrefix = "Loadout.Core.Teams.Capabilities.";

    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>
    /// The gates that exist. A capability may name one of these and no other.
    /// </summary>
    /// <remarks>
    /// Here rather than in a file, and that is the whole design. A capability
    /// says which decision governs it; what an agent may actually run is
    /// decided by this machine, in code, behind the same boundary as
    /// everything else. A file that could describe its own gate would be a
    /// shared file deciding what agents may execute, and a node has already
    /// been caught writing itself a record that claimed to be trusted.
    /// </remarks>
    /// <remarks>
    /// <para>
    /// Two, because two is how many real ones there are. Both are decided the
    /// same way: the shared half proposes and this machine disposes.
    /// </para>
    /// <para>
    /// <c>remedy</c> is per tool call, from a rule per kind of task and a
    /// fingerprint per exact script. <c>outward</c> is per named action - push,
    /// open a pull request - checked against what this machine lets a team
    /// allow, and enforced where a node's report claims it did one rather than
    /// at the call. Worth knowing which you are getting.
    /// </para>
    /// <para>
    /// Deliberately not here: the filesystem and network settings, which are a
    /// posture handed to the agent when it starts rather than a decision made
    /// per action; the per-node budget, which is a limit nobody is asked
    /// about; and the schedule and webhook, which govern whether a run happens
    /// at all rather than what a node may do with what a capability keeps.
    /// Naming any of them would promise a decision nothing makes.
    /// </para>
    /// </remarks>
    public static readonly IReadOnlySet<string> Gates =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "remedy", "outward" };

    /// <summary>
    /// The capabilities directory of each approved pack, asked for at load
    /// time.
    /// </summary>
    /// <remarks>
    /// Directories as given, not a root to look under. The team catalogue is
    /// handed pack <em>teams</em> directories and has to say where the
    /// capabilities are itself, and guessing a sibling inside here would be a
    /// rule about pack layout kept in the wrong place.
    /// </remarks>
    private readonly Func<CancellationToken, Task<IReadOnlyList<string>>> _packs;

    public CapabilityCatalogue(Func<CancellationToken, Task<IReadOnlyList<string>>>? packs = null) =>
        _packs = packs ?? (_ => Task.FromResult<IReadOnlyList<string>>([]));

    /// <summary>Reads every layer, nearest winning.</summary>
    public async Task<CapabilityCatalogueResult> LoadAsync(
        string? workspaceRoot,
        string? slug,
        CancellationToken ct = default)
    {
        var found = new Dictionary<string, DeclarationCapability>(StringComparer.OrdinalIgnoreCase);
        var origins = new Dictionary<string, SpecialistOrigin>(StringComparer.OrdinalIgnoreCase);
        var findings = new List<RuleFinding>();

        LoadBuiltIn(found, origins, findings);

        foreach (var directory in await _packs(ct).ConfigureAwait(false))
        {
            await LoadDirectoryAsync(
                directory, SpecialistOrigin.Pack, found, origins, findings, ct).ConfigureAwait(false);
        }

        if (workspaceRoot is { Length: > 0 })
        {
            await LoadDirectoryAsync(
                Path.Combine(workspaceRoot, "global", "capabilities"),
                SpecialistOrigin.Workspace, found, origins, findings, ct).ConfigureAwait(false);

            if (slug is { Length: > 0 })
            {
                await LoadDirectoryAsync(
                    Path.Combine(workspaceRoot, "projects", slug, "capabilities"),
                    SpecialistOrigin.Project, found, origins, findings, ct).ConfigureAwait(false);
            }
        }

        return new CapabilityCatalogueResult(found, findings, origins);
    }

    /// <summary>Reads one from its text, or says why the text is not one.</summary>
    public static DeclarationCapability? Parse(string text, string path, List<RuleFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);

        DeclarationCapability? capability;

        try
        {
            capability = Yaml.Deserialize<DeclarationCapability>(text ?? string.Empty);
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            findings.Add(new RuleFinding(
                path, RuleFindingSeverity.Error, "capability-unreadable",
                $"'{path}' is not readable as a capability: {ex.Message}"));

            return null;
        }

        if (capability is null || string.IsNullOrWhiteSpace(capability.Id))
        {
            findings.Add(new RuleFinding(
                path, RuleFindingSeverity.Error, "capability-id",
                $"'{path}' has no id, so nothing can ask for it."));

            return null;
        }

        // Named, never defined. A capability promising a decision nobody makes
        // would let a team declare something and believe it was governed.
        if (capability.Gate is { Length: > 0 } gate && !Gates.Contains(gate))
        {
            findings.Add(new RuleFinding(
                capability.Id, RuleFindingSeverity.Error, "capability-gate",
                $"Capability '{capability.Id}' names the gate '{gate}', which does not exist. "
                + $"A capability may name one of: {string.Join(", ", Gates.Order(StringComparer.Ordinal))}. "
                + "Gates are decided by this machine, in code, and cannot be described in a file."));

            return null;
        }

        return capability;
    }

    private static void LoadBuiltIn(
        Dictionary<string, DeclarationCapability> found,
        Dictionary<string, SpecialistOrigin> origins,
        List<RuleFinding> findings)
    {
        var assembly = typeof(CapabilityCatalogue).Assembly;

        foreach (var resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(BuiltInPrefix, StringComparison.Ordinal)
                || !resource.EndsWith(".yaml", StringComparison.Ordinal))
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(resource);

            if (stream is null)
            {
                continue;
            }

            using var reader = new StreamReader(stream, Encoding.UTF8);

            if (Parse(reader.ReadToEnd(), resource, findings) is { } capability)
            {
                found[capability.Id] = capability;
                origins[capability.Id] = SpecialistOrigin.BuiltIn;
            }
        }
    }

    private static async Task LoadDirectoryAsync(
        string directory,
        SpecialistOrigin origin,
        Dictionary<string, DeclarationCapability> found,
        Dictionary<string, SpecialistOrigin> origins,
        List<RuleFinding> findings,
        CancellationToken ct)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*.yaml")
            .OrderBy(one => one, StringComparer.Ordinal))
        {
            string text;

            try
            {
                text = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                findings.Add(new RuleFinding(
                    file, RuleFindingSeverity.Warning, "capability-unreadable",
                    $"'{file}' could not be read: {ex.Message}"));

                continue;
            }

            if (Parse(text, file, findings) is { } capability)
            {
                found[capability.Id] = capability;
                origins[capability.Id] = origin;
            }
        }
    }
}
