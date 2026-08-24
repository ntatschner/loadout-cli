namespace Loadout.Tui;

/// <summary>One thing the launcher can do, as the command line describes it.</summary>
/// <param name="Path">
/// What would be typed after <c>loadout</c>, such as <c>memory compress</c>.
/// </param>
/// <param name="Description">The one-line description the command declares.</param>
/// <param name="TerminalOnly">
/// Why this cannot usefully be run from a menu, or null when it can. A command
/// that emits a shell script to be piped somewhere, or that an agent invokes
/// rather than a person, is listed with its reason rather than hidden: a
/// command nobody can find is indistinguishable from one that does not exist.
/// </param>
public sealed record CatalogueEntry(string Path, string Description, string? TerminalOnly)
{
    /// <summary>True when running this from the launcher makes sense.</summary>
    public bool Runnable => TerminalOnly is null;

    /// <summary>The branch it belongs to, or empty for a top-level command.</summary>
    public string Group
    {
        get
        {
            var space = Path.IndexOf(' ', StringComparison.Ordinal);

            return space < 0 ? string.Empty : Path[..space];
        }
    }
}

/// <summary>
/// Every command the tool has, and a way to run one.
/// <para>
/// Built from the same registration the command line parses, rather than from a
/// second list kept alongside it. The launcher used to offer about a fifth of
/// what the command line could do, and closing that by hand would have created
/// exactly the kind of list that drifts — the allowlist guarding bare-name
/// launch had already done so, silently, four times in one sitting.
/// </para>
/// </summary>
public interface ICommandCatalogue
{
    /// <summary>Every registered command, in registration order.</summary>
    IReadOnlyList<CatalogueEntry> Commands { get; }

    /// <summary>
    /// Runs one, as though it had been typed. Returns its exit code.
    /// </summary>
    Task<int> RunAsync(string path, IReadOnlyList<string> arguments, CancellationToken ct = default);
}
