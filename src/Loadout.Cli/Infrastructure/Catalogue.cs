using System.ComponentModel;
using Loadout.Tui;
using Spectre.Console.Cli;

namespace Loadout.Cli.Infrastructure;

/// <summary>
/// The list of every command, built while they are registered.
/// <para>
/// The launcher offered about a fifth of what the command line could do, and
/// the obvious fix — placing each missing command in a menu by hand — creates a
/// second list that has to be kept in step with the first. That list had
/// already drifted once in this codebase, silently, and the four commands it
/// missed were reported as unknown project names rather than as anything wrong.
/// </para>
/// <para>
/// So the launcher reads this instead. A command that exists is in it by
/// construction, and a test asserts the two agree.
/// </para>
/// </summary>
internal static class Catalogue
{
    /// <summary>
    /// Commands that cannot usefully be run from a menu, and why.
    /// <para>
    /// Listed rather than hidden. Something a person cannot find is
    /// indistinguishable from something that does not exist, and "why is this
    /// not here" is a worse question than "why can this not run here".
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, string> TerminalOnly = new(StringComparer.Ordinal)
    {
        ["completion"] =
            "writes a shell completion script to standard output, which has to be piped "
            + "into a file or sourced by a shell",

        ["mcp serve"] =
            "is the MCP server itself, speaking a protocol over standard input and output. "
            + "An agent starts it; a person choosing it from a menu would be left looking at "
            + "a command that never returns",

        ["statusline"] =
            "renders the status line. The agent runs this itself, several times a minute",

        ["launch"] =
            "launches an agent against a project, which is what choosing a project here does",

        ["here"] =
            "launches the agent for the current repository, which is what this launcher is",
    };

    private static readonly List<CatalogueEntry> Entries = [];

    /// <summary>
    /// Guards the list. Recording is check-then-add, and anything building a
    /// parser on another thread races it: both callers see the path missing,
    /// both add it, and the palette then shows the command twice. Adding to a
    /// List from two threads can corrupt it outright rather than merely
    /// duplicating.
    /// </summary>
    private static readonly Lock Gate = new();

    /// <summary>Everything registered so far.</summary>
    internal static IReadOnlyList<CatalogueEntry> Commands
    {
        get
        {
            lock (Gate)
            {
                // Copied under the lock so a caller cannot be enumerating while
                // another thread appends.
                return [.. Entries];
            }
        }
    }

    /// <summary>
    /// Notes a branch — a name that groups sub-commands, such as
    /// <c>backup</c> — as something in its own right.
    /// </summary>
    /// <remarks>
    /// Only sub-commands were recorded before, so every listing of top-level
    /// commands silently omitted eleven whole families. Somebody looking for
    /// how to undo something types "backup", not "backup restore".
    /// </remarks>
    internal static void RecordBranch(
        string name,
        string description,
        string category,
        string intent)
    {
        TerminalOnly.TryGetValue(name, out var reason);

        lock (Gate)
        {
            if (Entries.Any(e => string.Equals(e.Path, name, StringComparison.Ordinal)))
            {
                return;
            }

            Entries.Add(new CatalogueEntry(name, description, reason, category, intent));
        }
    }

    /// <summary>
    /// The argument a command cannot run without, or empty when it can run bare.
    /// </summary>
    /// <remarks>
    /// Read off the settings type as the command is registered, so it cannot
    /// disagree with what the parser will demand. Spectre writes a required
    /// argument in angle brackets and an optional one in square brackets, and
    /// the attribute says which it is outright.
    /// </remarks>
    private static string RequiredArgument(Type command)
    {
        var settings = command.BaseType?.GetGenericArguments().FirstOrDefault();

        if (settings is null)
        {
            return string.Empty;
        }

        foreach (var property in settings.GetProperties())
        {
            var argument = property
                .GetCustomAttributes(typeof(CommandArgumentAttribute), inherit: true)
                .OfType<CommandArgumentAttribute>()
                .FirstOrDefault();

            if (argument is { IsRequired: true })
            {
                return argument.ValueName.Trim('<', '>', '[', ']');
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Notes one command. Called as it is registered with the parser, so the
    /// two cannot disagree.
    /// </summary>
    internal static void Record(string path, Type command)
    {
        var description = command
            .GetCustomAttributes(typeof(DescriptionAttribute), inherit: false)
            .OfType<DescriptionAttribute>()
            .FirstOrDefault()?
            .Description ?? string.Empty;

        // Matched on the first word so a whole branch can be excluded at once,
        // which is what the statusline branch needs.
        var group = path.Split(' ')[0];

        TerminalOnly.TryGetValue(group, out var reason);

        lock (Gate)
        {
            // Registration happens once per process, but a test may build the
            // parser more than once and duplicates would double every menu.
            if (Entries.Any(e => string.Equals(e.Path, path, StringComparison.Ordinal)))
            {
                return;
            }

            var meta = command
                .GetCustomAttributes(typeof(CommandMetaAttribute), inherit: false)
                .OfType<CommandMetaAttribute>()
                .FirstOrDefault();

            Entries.Add(new CatalogueEntry(
                path,
                description,
                reason,
                meta?.Category ?? string.Empty,
                meta?.Intent ?? string.Empty,
                meta?.Mutates ?? false,
                meta?.RequiresNetwork ?? false,
                meta?.Example ?? string.Empty,
                RequiredArgument(command)));
        }
    }
}

/// <summary>
/// Runs a command chosen from the launcher, as though it had been typed.
/// <para>
/// The same parser, so a command behaves identically wherever it was started
/// from and there is no second implementation of anything to keep in step.
/// </para>
/// </summary>
internal sealed class CommandCatalogue : ICommandCatalogue
{
    private readonly Func<string[], Task<int>> _run;

    internal CommandCatalogue(Func<string[], Task<int>> run) => _run = run;

    /// <inheritdoc />
    public IReadOnlyList<CatalogueEntry> Commands => Catalogue.Commands;

    /// <inheritdoc />
    public Task<int> RunAsync(
        string path,
        IReadOnlyList<string> arguments,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string[] parts = [.. path.Split(' ', StringSplitOptions.RemoveEmptyEntries), .. arguments];

        return _run(parts);
    }
}
