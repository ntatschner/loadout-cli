using System.Text.Json;
using System.Text.RegularExpressions;
using Loadout.Core.Git;
using Loadout.Models.Projects;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Instructions;

/// <summary>
/// What a project has said about how its code should be read.
/// </summary>
/// <param name="Ignore">Globs, repository-relative, for files the scan should leave out.</param>
/// <param name="Extensions">
/// Extra extensions mapped onto a language the scan knows, such as
/// <c>.pyw</c> onto <c>python</c>. Lowercased with the dot.
/// </param>
/// <param name="Languages">Languages the project has described itself, ahead of the built-in table.</param>
public sealed record SymbolScanOptions(
    IReadOnlyList<string> Ignore,
    IReadOnlyDictionary<string, string> Extensions,
    IReadOnlyList<SymbolLanguage> Languages)
{
    /// <summary>Nothing said: the built-in table and no exclusions.</summary>
    public static readonly SymbolScanOptions Default =
        new([], new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), []);

    /// <summary>
    /// The language for a file, by the project's mappings first and the
    /// table second, or null when neither reads it.
    /// </summary>
    public SymbolLanguage? LanguageFor(string file)
    {
        var extension = Path.GetExtension(file);

        if (extension.Length == 0)
        {
            return null;
        }

        foreach (var language in Languages)
        {
            if (language.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                return language;
            }
        }

        if (Extensions.TryGetValue(extension, out var id))
        {
            var mapped = Languages.FirstOrDefault(l => l.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                ?? SymbolLanguages.All.FirstOrDefault(l => l.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

            if (mapped is not null)
            {
                return mapped;
            }
        }

        return SymbolLanguages.For(file);
    }

    /// <summary>Whether a repository-relative path is one the project asked to leave out.</summary>
    public bool Excludes(string file) =>
        Ignore.Any(glob => RuleService.Matches(glob, file));

    /// <summary>
    /// Options from a manifest's <c>symbols</c> section.
    /// </summary>
    /// <remarks>
    /// A pattern that does not compile is dropped with the language it was
    /// part of rather than failing the scan: a mistake in one project's
    /// manifest should cost that project its custom language, not every
    /// lookup. Which is why the manifest section says to run <c>docs find</c>
    /// once after writing one.
    /// </remarks>
    public static SymbolScanOptions From(ProjectSymbols? symbols)
    {
        if (symbols is null)
        {
            return Default;
        }

        var extensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (extension, id) in symbols.Extensions)
        {
            if (extension is { Length: > 0 } && id is { Length: > 0 })
            {
                extensions[extension.StartsWith('.') ? extension : "." + extension] = id;
            }
        }

        var languages = new List<SymbolLanguage>();

        foreach (var described in symbols.Languages)
        {
            if (described.Id is not { Length: > 0 }
                || described.Extensions.Count == 0
                || described.Members is not { Length: > 0 })
            {
                continue;
            }

            try
            {
                languages.Add(new SymbolLanguage(
                    described.Id,
                    described.Name is { Length: > 0 } ? described.Name : described.Id,
                    [.. described.Extensions.Select(e => e.StartsWith('.') ? e : "." + e)],
                    described.Types is { Length: > 0 } ? new Regex(described.Types, RegexOptions.CultureInvariant) : null,
                    new Regex(described.Members, RegexOptions.CultureInvariant),
                    Enum.TryParse<DocStyle>(
                        described.Docs.Replace("_", string.Empty, StringComparison.Ordinal),
                        ignoreCase: true,
                        out var style)
                        ? style
                        : DocStyle.Hash));
            }
            catch (ArgumentException)
            {
                // A pattern that does not compile. Dropped, as the remarks say.
            }
        }

        return new SymbolScanOptions([.. symbols.Ignore.Where(g => g is { Length: > 0 })], extensions, languages);
    }
}

/// <summary>Lists the files a scan should read.</summary>
public interface ISymbolSourceLister
{
    /// <summary>
    /// Repository-relative, forward-slashed paths, or null when the tree is
    /// not something that can say which files are its own.
    /// </summary>
    Task<IReadOnlyList<string>?> ListAsync(string repositoryPath, CancellationToken ct = default);
}

/// <summary>
/// The files git considers the project's: tracked, plus present and not
/// ignored.
/// </summary>
/// <remarks>
/// <para>
/// A hand-kept list of directory names to skip is always one short. It left
/// this repository's own scripts out, because they live under <c>build</c>,
/// and let a parked virtual environment in, because it lived under a name
/// nobody had thought of. The project has already written down what is its
/// own, in <c>.gitignore</c>, and git applies it exactly. Two processes at
/// scan time, run together, is what that costs; a lookup from the cache pays
/// nothing.
/// </para>
/// <para>
/// Outside a repository there is no such list, and the caller walks the tree
/// with the old rules instead.
/// </para>
/// </remarks>
public sealed class GitSourceLister : ISymbolSourceLister
{
    private readonly IGitManager _git;

    public GitSourceLister(IGitManager git)
    {
        _git = git;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>?> ListAsync(string repositoryPath, CancellationToken ct = default)
    {
        if (GitHead.WorkingTreeRoot(repositoryPath) is null)
        {
            return null;
        }

        var tracked = _git.ListFilesAsync(repositoryPath, ["."], GitFileSet.Tracked, ct);
        var untracked = _git.ListFilesAsync(repositoryPath, ["."], GitFileSet.UntrackedAndVisible, ct);

        await Task.WhenAll(tracked, untracked).ConfigureAwait(false);

        var listedTracked = await tracked.ConfigureAwait(false);
        var listedUntracked = await untracked.ConfigureAwait(false);

        if (listedTracked.Failed)
        {
            return null;
        }

        var files = new List<string>(listedTracked.Value!);

        if (listedUntracked.Succeeded)
        {
            files.AddRange(listedUntracked.Value!);
        }

        return
        [
            .. files
                .Select(file => file.Replace('\\', '/'))
                .Where(file => file.Length > 0)
                .Distinct(StringComparer.Ordinal),
        ];
    }
}

/// <summary>Names the symbols in files the built-in table cannot read.</summary>
public interface ISymbolTagger
{
    /// <summary>
    /// Symbols for the files given, or null when nothing on this machine can
    /// produce them. An empty list is an answer; null is not.
    /// </summary>
    Task<IReadOnlyList<Symbol>?> TagAsync(
        string repositoryPath,
        IReadOnlyList<string> files,
        CancellationToken ct = default);
}

/// <summary>
/// Universal Ctags, when it is installed, for the languages the table is not.
/// </summary>
/// <remarks>
/// <para>
/// The table reads sixteen languages; ctags reads well over a hundred, and
/// this launcher already drives the tools on the machine rather than
/// bundling them. So it is used for exactly the files the table would skip,
/// and only when it is there: a machine without it gets the table, which is
/// what it got before, and no message about a tool it never had.
/// </para>
/// <para>
/// The table keeps the files it knows, because it also reads the comment
/// that documents a declaration and ctags does not. The two halves are
/// joined by file, never by symbol, so nothing is counted twice.
/// </para>
/// </remarks>
public sealed class CtagsTagger : ISymbolTagger
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private readonly IProcessLauncher _processes;
    private readonly IExecutableResolver _executables;

    public CtagsTagger(IProcessLauncher processes, IExecutableResolver executables)
    {
        _processes = processes;
        _executables = executables;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Symbol>?> TagAsync(
        string repositoryPath,
        IReadOnlyList<string> files,
        CancellationToken ct = default)
    {
        if (files.Count == 0)
        {
            return [];
        }

        var ctags = _executables.Resolve("ctags");

        if (ctags is null)
        {
            return null;
        }

        // Files on stdin rather than the tree: the same list the table was
        // given, so the two halves agree about what the project is.
        var request = new ProcessRequest(
            ctags,
            ["--output-format=json", "--fields=+nl", "-L", "-", "-f", "-"],
            repositoryPath,
            StandardInput: string.Join('\n', files) + "\n");

        var run = await _processes.RunAsync(request, Timeout, ct).ConfigureAwait(false);

        if (run.Failed || !run.Value!.Succeeded)
        {
            // Exuberant ctags, which cannot write JSON, exits here with a
            // usage error; so does a Universal Ctags too old for the option.
            // Either is no tagger rather than a broken one.
            return null;
        }

        return CtagsOutput.Parse(run.Value.StandardOutput);
    }
}

/// <summary>
/// Reads what Universal Ctags writes as JSON lines.
/// </summary>
/// <remarks>
/// Kinds are folded onto the two this index has. Anything that holds
/// members is a type; anything callable or addressable inside one is a
/// member; the rest — locals, labels, includes — is not indexed, because
/// nobody asks where a local variable is.
/// </remarks>
public static class CtagsOutput
{
    private static readonly HashSet<string> TypeKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "class", "struct", "interface", "enum", "union", "trait", "module", "namespace",
        "type", "typedef", "typealias", "record", "protocol", "package", "object",
        "annotation", "table", "view", "schema", "component", "resource",
    };

    private static readonly HashSet<string> MemberKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "function", "method", "member", "field", "property", "procedure", "func",
        "subroutine", "constructor", "getter", "setter", "operator", "macro",
        "constant", "enumerator", "def", "fn", "alias", "signal", "slot", "prototype",
    };

    /// <summary>Symbols from ctags JSON lines. Lines that are not tags are skipped.</summary>
    public static IReadOnlyList<Symbol> Parse(string output)
    {
        var symbols = new List<Symbol>();

        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();

            if (trimmed.Length == 0 || trimmed[0] != '{')
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(trimmed);
                var root = document.RootElement;

                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("_type", out var type)
                    || type.GetString() != "tag"
                    || !root.TryGetProperty("name", out var name)
                    || !root.TryGetProperty("path", out var path)
                    || !root.TryGetProperty("kind", out var kind))
                {
                    continue;
                }

                var kindName = kind.GetString() ?? string.Empty;

                SymbolKind? folded = TypeKinds.Contains(kindName) ? SymbolKind.Type
                    : MemberKinds.Contains(kindName) ? SymbolKind.Member
                    : null;

                if (folded is null)
                {
                    continue;
                }

                var lineNumber = root.TryGetProperty("line", out var at) && at.TryGetInt32(out var number)
                    ? number
                    : 0;

                var language = root.TryGetProperty("language", out var lang)
                    ? (lang.GetString() ?? string.Empty).ToLowerInvariant()
                    : string.Empty;

                symbols.Add(new Symbol(
                    folded.Value,
                    name.GetString() ?? string.Empty,
                    string.Empty,
                    (path.GetString() ?? string.Empty).Replace('\\', '/'),
                    lineNumber,
                    string.Empty,
                    string.Empty,
                    language));
            }
            catch (JsonException)
            {
                // One bad line is one bad line.
            }
        }

        // One name at one line is one symbol. Haskell reports a data type and
        // its constructor of the same name on the same line, and a lookup
        // that returned both would look like it had found two things.
        return
        [
            .. symbols
                .Where(symbol => symbol.Name.Length > 0 && symbol.File.Length > 0 && symbol.Line > 0)
                .GroupBy(symbol => (symbol.File, symbol.Line, symbol.Name))
                .Select(group => group.OrderBy(symbol => symbol.Kind == SymbolKind.Type ? 0 : 1).First())
                .OrderBy(symbol => symbol.File, StringComparer.Ordinal)
                .ThenBy(symbol => symbol.Line),
        ];
    }
}
