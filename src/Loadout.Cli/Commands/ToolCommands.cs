using System.ComponentModel;
using System.Globalization;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Configuration;
using Loadout.Core.Teams;
using Loadout.Core.Tools;
using Loadout.Models;
using Loadout.Models.Configuration;
using Loadout.Models.Tools;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>What every <c>tools</c> command shares.</summary>
public class ToolSettings : GlobalSettings
{
}

/// <summary>
/// Parsing and shaping shared by the <c>tools</c> commands and the MCP tools
/// that make the same calls.
/// </summary>
/// <remarks>
/// One place for the shapes, so what an agent is told over MCP and what a
/// person reads with <c>--json</c> cannot drift apart.
/// </remarks>
public static class ToolShapes
{
    /// <summary>Splits <c>name@version</c>; the version is null where none was given.</summary>
    public static (string Name, string? Version) Split(string named)
    {
        var at = (named ?? string.Empty).IndexOf('@', StringComparison.Ordinal);

        return at < 0 ? (named ?? string.Empty, null) : (named![..at], named[(at + 1)..]);
    }

    /// <summary>One tool as a search result.</summary>
    public static object Found(ToolRecord one) => new
    {
        name = one.Name,
        kind = one.Kind,
        summary = one.Summary,
        lifecycle = one.Lifecycle,
        active = one.Active,
        capabilities = one.Capabilities,
        replacement = one.Deprecated?.Replacement is { Length: > 0 } instead ? instead : null,
    };

    /// <summary>One tool in full, and whether this machine trusts the version asked about.</summary>
    public static object Shown(ToolShown shown, string? version, bool trusted) => new
    {
        name = shown.Record.Name,
        kind = shown.Record.Kind,
        owner = shown.Record.Owner,
        summary = shown.Record.Summary,
        lifecycle = shown.Record.Lifecycle,
        active = shown.Record.Active,
        version = version ?? shown.Record.Active,
        versions = shown.Versions,
        trusted,
        deprecated = shown.Record.Deprecated,
        lineage = shown.Record.Lineage,
        usage = shown.Record.UsageSummary,
        manifest = shown.Active,
    };

    /// <summary>Capabilities from a comma-separated list, or null for none.</summary>
    public static IReadOnlyList<string>? Words(string? list) =>
        list is { Length: > 0 }
            ? [.. list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
            : null;

    /// <summary>Whether this machine agreed to that version, at the script it has now.</summary>
    public static bool Trusted(IToolRegistry registry, MachineConfig? machine, string name, string? version) =>
        version is { Length: > 0 }
        && registry.ScriptOf(name, version) is { } script
        && (machine?.Teams.TrustedTools ?? []).Any(one =>
            string.Equals(one.Tool, name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(one.Version, version, StringComparison.Ordinal)
            && string.Equals(one.Fingerprint, RemedyCeiling.Fingerprint(script), StringComparison.OrdinalIgnoreCase));
}

/// <summary>Search the machine's shared tools.</summary>
[Description("Search the tools this machine shares between teams.")]
[CommandMeta(CommandCategory.Start,
    Intent = "tools search catalogue registry shared reusable find existing",
    Example = "loadout tools search cache disk")]
public sealed class ToolSearchCommand : Command<ToolSearchCommand.Settings>
{
    private readonly IToolRegistry _registry;
    private readonly IAnsiConsole _console;

    public ToolSearchCommand(IToolRegistry registry, IAnsiConsole console)
    {
        _registry = registry;
        _console = console;
    }

    public sealed class Settings : ToolSettings
    {
        [CommandArgument(0, "[WORDS]")]
        [Description("What you are looking for. Every tool when omitted.")]
        public string[] Words { get; init; } = [];

        [CommandOption("--kind <KIND>")]
        [Description("Only tools of this kind, such as disk or service.")]
        public string Kind { get; init; } = string.Empty;

        [CommandOption("--all")]
        [Description("Include deprecated and retired tools, each with what replaces it.")]
        public bool All { get; init; }
    }

    /// <inheritdoc />
    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);
        var words = string.Join(' ', settings.Words);
        var found = _registry.Search(words, settings.All)
            .Where(one => settings.Kind.Length == 0 || string.Equals(one.Kind, settings.Kind, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (output.IsJson)
        {
            output.WriteJson(new { query = words, root = _registry.Root(), tools = found.Select(ToolShapes.Found) });

            return CommandOutput.Success();
        }

        if (found.Count == 0)
        {
            output.WriteLine("[dim]Nothing in the catalogue matched. It matches words, not meanings.[/]");

            return CommandOutput.Success();
        }

        foreach (var one in found)
        {
            output.WriteLine(
                $"[bold]{Markup.Escape(one.Name)}[/] [dim]{Markup.Escape(one.Kind)} {Markup.Escape(one.Lifecycle)}"
                + (one.Active is { Length: > 0 } active ? $" v{Markup.Escape(active)}" : string.Empty) + "[/]");
            output.WriteLine($"  {Shown.Safely(one.Summary)}");
        }

        return CommandOutput.Success();
    }
}

/// <summary>One shared tool in full.</summary>
[Description("Show one shared tool: its manifest, versions, usage, lineage and trust here.")]
[CommandMeta(CommandCategory.Start,
    Intent = "tools show manifest version lineage usage trusted",
    Example = "loadout tools show free-disk-by-cache@1.2")]
public sealed class ToolShowCommand : AsyncCommand<ToolShowCommand.Settings>
{
    private readonly IToolRegistry _registry;
    private readonly IConfigurationService _configuration;
    private readonly IAnsiConsole _console;

    public ToolShowCommand(IToolRegistry registry, IConfigurationService configuration, IAnsiConsole console)
    {
        _registry = registry;
        _configuration = configuration;
        _console = console;
    }

    public sealed class Settings : ToolSettings
    {
        [CommandArgument(0, "<TOOL>")]
        [Description("The tool, as name or name@version.")]
        public string Tool { get; init; } = string.Empty;
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);
        var (name, version) = ToolShapes.Split(settings.Tool);
        var shown = _registry.Show(name);

        if (shown.Failed)
        {
            return output.Fail(shown);
        }

        var machine = await _configuration.LoadMachineAsync(cancellationToken).ConfigureAwait(false);
        var asked = version ?? shown.Value!.Record.Active;
        var trusted = ToolShapes.Trusted(_registry, machine.Value, name, asked);

        if (output.IsJson)
        {
            output.WriteJson(ToolShapes.Shown(shown.Value!, asked, trusted));

            return CommandOutput.Success();
        }

        var head = shown.Value!.Record;
        output.WriteLine($"[bold]{Markup.Escape(head.Name)}[/] [dim]{Markup.Escape(head.Kind)} {Markup.Escape(head.Lifecycle)}[/]");
        output.WriteLine($"  {Shown.Safely(head.Summary)}");

        foreach (var (one, standing) in shown.Value!.Versions)
        {
            output.WriteLine($"  v{Markup.Escape(one)} [dim]{Markup.Escape(standing)}{(one == head.Active ? ", active" : string.Empty)}[/]");
        }

        if (shown.Value!.Active is { } active)
        {
            output.WriteLine($"  [dim]Purpose:[/] {Shown.Safely(active.Purpose)}");
            output.WriteLine($"  [dim]Origin:[/] {Shown.Safely(active.Origin)}");
        }

        output.WriteLine(
            $"  [dim]Used {head.UsageSummary.Runs} times: {head.UsageSummary.Ok} ok, {head.UsageSummary.Failed} failed, "
            + $"{head.UsageSummary.Workaround} worked around.[/]");
        output.WriteLine(trusted
            ? $"  [green]v{Markup.Escape(asked ?? string.Empty)} is trusted on this machine.[/]"
            : "  [dim]Not trusted on this machine. A remediator asks before running it.[/]");

        return CommandOutput.Success();
    }
}

/// <summary>Sending a candidate, idea, bug or lesson to the catalogue.</summary>
[Description("Submit a candidate tool, an idea, a bug or a lesson to the shared catalogue's inbox.")]
[CommandMeta(CommandCategory.Start,
    Intent = "tools submit candidate idea bug lesson inbox propose", Mutates = true,
    Example = "loadout tools submit --kind lesson --text \"Clearing the build cache fixed a full disk twice.\"")]
public sealed class ToolSubmitCommand : Command<ToolSubmitCommand.Settings>
{
    private readonly IToolRegistry _registry;
    private readonly IAnsiConsole _console;

    public ToolSubmitCommand(IToolRegistry registry, IAnsiConsole console)
    {
        _registry = registry;
        _console = console;
    }

    public sealed class Settings : ToolSettings
    {
        [CommandOption("--kind <KIND>")]
        [Description("candidate, idea, bug or lesson.")]
        public string Kind { get; init; } = string.Empty;

        [CommandOption("--text <TEXT>")]
        [Description("What it says. Screened for credentials, and refused if it holds one.")]
        public string Text { get; init; } = string.Empty;

        [CommandOption("--tool <NAME>")]
        [Description("The tool it is about, where it is about one.")]
        public string Tool { get; init; } = string.Empty;

        [CommandOption("--from-run <ID>")]
        [Description("The run it came from.")]
        public string Run { get; init; } = string.Empty;

        [CommandOption("--script <FILE>")]
        [Description("A candidate's script, checked for overlap with the tools already here.")]
        public string Script { get; init; } = string.Empty;

        [CommandOption("--summary <TEXT>")]
        [Description("A candidate's one-line summary.")]
        public string Summary { get; init; } = string.Empty;

        [CommandOption("--capabilities <WORDS>")]
        [Description("A candidate's search terms, separated by commas.")]
        public string Capabilities { get; init; } = string.Empty;
    }

    /// <inheritdoc />
    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        if (settings.Text is not { Length: > 0 })
        {
            return output.Fail("Say what it is with --text.", ExitCode.InvalidArguments);
        }

        string? script = null;

        if (settings.Script is { Length: > 0 } file)
        {
            try
            {
                script = File.ReadAllText(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return output.Fail($"{file} could not be read: {ex.Message}", ExitCode.InvalidArguments);
            }
        }

        if (settings.DryRun)
        {
            if (output.IsJson)
            {
                output.WriteJson(new { dry_run = true, kind = settings.Kind, tool = settings.Tool });
            }
            else
            {
                output.WriteLine($"[dim]Dry run: nothing was changed.[/] A {Markup.Escape(settings.Kind)} would be submitted.");
            }

            return CommandOutput.Success();
        }

        var submitted = _registry.Submit(new ToolSubmission(
            settings.Kind,
            settings.Text,
            settings.Tool is { Length: > 0 } tool ? tool : null,
            Environment.UserName,
            settings.Run is { Length: > 0 } run ? run : null,
            script,
            ToolShapes.Words(settings.Capabilities),
            settings.Summary is { Length: > 0 } summary ? summary : null));

        if (submitted.Failed)
        {
            return output.Fail(submitted);
        }

        if (output.IsJson)
        {
            output.WriteJson(new { id = submitted.Value!.Id, kind = settings.Kind.Trim().ToLowerInvariant(), overlapping = submitted.Value!.Overlapping });

            return CommandOutput.Success();
        }

        output.WriteLine($"[green]Submitted as {Markup.Escape(submitted.Value!.Id)}.[/]");

        if (submitted.Value!.Overlapping.Count > 0)
        {
            output.WriteLine(
                $"[yellow]It overlaps {Markup.Escape(string.Join(", ", submitted.Value!.Overlapping))}.[/] "
                + "Extending that is usually better than a second tool beside it.");
        }

        return CommandOutput.Success();
    }
}

/// <summary>Saying how a use of a shared tool went.</summary>
[Description("Record a use of a shared tool: whether it worked, failed, or needed working around.")]
[CommandMeta(CommandCategory.Start,
    Intent = "tools used usage outcome failed workaround record", Mutates = true,
    Example = "loadout tools used free-disk-by-cache@1.2 --outcome ok")]
public sealed class ToolUsedCommand : Command<ToolUsedCommand.Settings>
{
    private readonly IToolRegistry _registry;
    private readonly IAnsiConsole _console;

    public ToolUsedCommand(IToolRegistry registry, IAnsiConsole console)
    {
        _registry = registry;
        _console = console;
    }

    public sealed class Settings : ToolSettings
    {
        [CommandArgument(0, "<TOOL>")]
        [Description("The tool and the version used, as name@version.")]
        public string Tool { get; init; } = string.Empty;

        [CommandOption("--outcome <OUTCOME>")]
        [Description("ok, failed or workaround.")]
        public string Outcome { get; init; } = ToolOutcome.Ok;

        [CommandOption("--run <ID>")]
        [Description("The run it was used in.")]
        public string Run { get; init; } = string.Empty;

        [CommandOption("--team <TEAM>")]
        [Description("The team that used it.")]
        public string Team { get; init; } = string.Empty;

        [CommandOption("--note <TEXT>")]
        [Description("What happened, where it did not simply work.")]
        public string Note { get; init; } = string.Empty;
    }

    /// <inheritdoc />
    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);
        var (name, version) = ToolShapes.Split(settings.Tool);

        if (settings.DryRun)
        {
            if (output.IsJson)
            {
                output.WriteJson(new { dry_run = true, tool = name, version, outcome = settings.Outcome });
            }
            else
            {
                output.WriteLine($"[dim]Dry run: nothing was changed.[/] A use of {Markup.Escape(settings.Tool)} would be recorded.");
            }

            return CommandOutput.Success();
        }

        var recorded = _registry.RecordUsage(new ToolUsage
        {
            Tool = name,
            Version = version ?? string.Empty,
            Outcome = settings.Outcome,
            Run = settings.Run,
            Team = settings.Team,
            Note = settings.Note,
        });

        if (recorded.Failed)
        {
            return output.Fail(recorded);
        }

        if (output.IsJson)
        {
            output.WriteJson(new { tool = name, version, outcome = settings.Outcome, recorded = true });
        }
        else
        {
            output.WriteLine($"[green]Recorded: {Markup.Escape(settings.Tool)} {Markup.Escape(settings.Outcome)}.[/]");
        }

        return CommandOutput.Success();
    }
}

/// <summary>What has happened to the catalogue.</summary>
[Description("Read the shared catalogue's audit log: submissions, verifies, promotions, deprecations, trust.")]
[CommandMeta(CommandCategory.Start,
    Intent = "tools audit log history promote deprecate who when",
    Example = "loadout tools audit --since 7d")]
public sealed class ToolAuditCommand : Command<ToolAuditCommand.Settings>
{
    private readonly IToolRegistry _registry;
    private readonly IAnsiConsole _console;

    public ToolAuditCommand(IToolRegistry registry, IAnsiConsole console)
    {
        _registry = registry;
        _console = console;
    }

    public sealed class Settings : ToolSettings
    {
        [CommandOption("--tool <NAME>")]
        [Description("Only this tool.")]
        public string Tool { get; init; } = string.Empty;

        [CommandOption("--since <AGE>")]
        [Description("Only entries this recent, in days, such as 7d.")]
        public string Since { get; init; } = string.Empty;
    }

    /// <inheritdoc />
    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);
        DateTimeOffset? since = null;

        if (settings.Since is { Length: > 0 } age)
        {
            if (!int.TryParse(age.TrimEnd('d', 'D'), NumberStyles.None, CultureInfo.InvariantCulture, out var days))
            {
                return output.Fail($"'{age}' is not an age in days, such as 7d.", ExitCode.InvalidArguments);
            }

            since = DateTimeOffset.UtcNow.AddDays(-days);
        }

        var entries = _registry.Audit(settings.Tool is { Length: > 0 } tool ? tool : null, since);

        if (output.IsJson)
        {
            output.WriteJson(new { entries });

            return CommandOutput.Success();
        }

        foreach (var one in entries)
        {
            output.WriteLine(
                $"[dim]{one.At:yyyy-MM-dd HH:mm}[/] {Markup.Escape(one.Action)} {Markup.Escape(one.Tool)}"
                + (one.Version is { Length: > 0 } v ? $"@{Markup.Escape(v)}" : string.Empty)
                + (one.Note is { Length: > 0 } note ? $" [dim]{Shown.Safely(note)}[/]" : string.Empty));
        }

        return CommandOutput.Success();
    }
}

/// <summary>Running a draft's harness and the regression gate.</summary>
[Description("Verify a draft: run its harness and the known-good cases, where this machine allows it.")]
[CommandMeta(CommandCategory.Start,
    Intent = "tools verify harness test regression gate draft", Mutates = true,
    Example = "loadout tools verify <state>/tools/drafts/free-disk-by-cache/1")]
public sealed class ToolVerifyCommand : AsyncCommand<ToolVerifyCommand.Settings>
{
    private readonly IToolRegistry _registry;
    private readonly IConfigurationService _configuration;
    private readonly IAnsiConsole _console;

    public ToolVerifyCommand(IToolRegistry registry, IConfigurationService configuration, IAnsiConsole console)
    {
        _registry = registry;
        _configuration = configuration;
        _console = console;
    }

    public sealed class Settings : ToolSettings
    {
        [CommandArgument(0, "<DRAFT>")]
        [Description("The draft's directory, under the catalogue's drafts.")]
        public string Draft { get; init; } = string.Empty;
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        if (settings.DryRun)
        {
            if (output.IsJson)
            {
                output.WriteJson(new { dry_run = true, draft = settings.Draft });
            }
            else
            {
                output.WriteLine($"[dim]Dry run: nothing was run or changed.[/] {Markup.Escape(settings.Draft)} would be verified.");
            }

            return CommandOutput.Success();
        }

        // What this machine says about running harnesses, and what a person
        // agreed to, from this machine's configuration and nowhere else.
        var machine = await _configuration.LoadMachineAsync(cancellationToken).ConfigureAwait(false);

        if (machine.Failed)
        {
            return output.Fail(machine);
        }

        var teams = machine.Value!.Teams;
        var consent = new ToolTestConsent(
            teams.Remediation.TryGetValue(ToolHarness.Kind, out var rule) ? rule : null,
            teams.TrustedRemedies);

        var verified = await _registry.VerifyAsync(settings.Draft, consent, cancellationToken).ConfigureAwait(false);

        if (verified.Failed)
        {
            return output.Fail(verified);
        }

        var result = verified.Value!;

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                ruling = result.Ruling.ToString().ToLowerInvariant(),
                because = result.Because,
                passed = result.Gate?.Passed,
            });

            return CommandOutput.Success();
        }

        output.WriteLine(result.Gate is { Passed: true }
            ? $"[green]Verified.[/] {Shown.Safely(result.Because)}"
            : $"[yellow]Not verified.[/] {Shown.Safely(result.Because)}");

        return CommandOutput.Success();
    }
}

/// <summary>Making a verified draft a version.</summary>
[Description("Promote a verified draft to a written-once version and make it active.")]
[CommandMeta(CommandCategory.Start,
    Intent = "tools promote version active known-good release draft", Mutates = true,
    Example = "loadout tools promote <draft> --because lesson --source inbox/2026-09-23-7f3a")]
public sealed class ToolPromoteCommand : Command<ToolPromoteCommand.Settings>
{
    private readonly IToolRegistry _registry;
    private readonly IAnsiConsole _console;

    public ToolPromoteCommand(IToolRegistry registry, IAnsiConsole console)
    {
        _registry = registry;
        _console = console;
    }

    public sealed class Settings : ToolSettings
    {
        [CommandArgument(0, "<DRAFT>")]
        [Description("The draft's directory, under the catalogue's drafts.")]
        public string Draft { get; init; } = string.Empty;

        [CommandOption("--because <WHY>")]
        [Description("lesson, requirement, bug, idea, nomination or consolidation.")]
        public string Because { get; init; } = string.Empty;

        [CommandOption("--source <WHERE>")]
        [Description("The submission or run this version came from.")]
        public string Source { get; init; } = string.Empty;

        [CommandOption("--owner <TEAM>")]
        [Description("The team that maintains it, for a new tool.")]
        public string Owner { get; init; } = string.Empty;

        [CommandOption("--kind <KIND>")]
        [Description("The kind this machine's remediation rules decide on, for a new tool.")]
        public string Kind { get; init; } = string.Empty;

        [CommandOption("--summary <TEXT>")]
        [Description("One line saying what it is for.")]
        public string Summary { get; init; } = string.Empty;

        [CommandOption("--capabilities <WORDS>")]
        [Description("Search terms, separated by commas.")]
        public string Capabilities { get; init; } = string.Empty;
    }

    /// <inheritdoc />
    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        if (settings.Because is not { Length: > 0 } || settings.Source is not { Length: > 0 })
        {
            return output.Fail(
                "Say why the version exists with --because and where it came from with --source.",
                ExitCode.InvalidArguments);
        }

        if (settings.DryRun)
        {
            if (output.IsJson)
            {
                output.WriteJson(new { dry_run = true, draft = settings.Draft });
            }
            else
            {
                output.WriteLine($"[dim]Dry run: nothing was changed.[/] {Markup.Escape(settings.Draft)} would be promoted.");
            }

            return CommandOutput.Success();
        }

        var promoted = _registry.Promote(settings.Draft, new ToolPromotionRequest(
            settings.Because,
            settings.Source,
            Environment.UserName,
            Owner: settings.Owner is { Length: > 0 } owner ? owner : null,
            Kind: settings.Kind is { Length: > 0 } kind ? kind : null,
            Summary: settings.Summary is { Length: > 0 } summary ? summary : null,
            Capabilities: ToolShapes.Words(settings.Capabilities)));

        if (promoted.Failed)
        {
            return output.Fail(promoted);
        }

        if (output.IsJson)
        {
            output.WriteJson(new { name = promoted.Value!.Name, version = promoted.Value!.Version, fingerprint = promoted.Value!.Fingerprint });
        }
        else
        {
            output.WriteLine($"[green]{Markup.Escape(promoted.Value!.Name)}@{Markup.Escape(promoted.Value!.Version)} is promoted and active.[/]");
            output.WriteLine("[dim]Nobody has trusted it yet: a remediator asks before running it.[/]");
        }

        return CommandOutput.Success();
    }
}

/// <summary>Marking a tool as not to be used any more.</summary>
[Description("Deprecate a shared tool, naming what replaces it or why.")]
[CommandMeta(CommandCategory.Start,
    Intent = "tools deprecate replace obsolete lifecycle", Mutates = true,
    Example = "loadout tools deprecate free-disk-by-cache --replacement cache-sweeper")]
public sealed class ToolDeprecateCommand : Command<ToolDeprecateCommand.Settings>
{
    private readonly IToolRegistry _registry;
    private readonly IAnsiConsole _console;

    public ToolDeprecateCommand(IToolRegistry registry, IAnsiConsole console)
    {
        _registry = registry;
        _console = console;
    }

    public sealed class Settings : ToolSettings
    {
        [CommandArgument(0, "<TOOL>")]
        [Description("The tool.")]
        public string Tool { get; init; } = string.Empty;

        [CommandOption("--replacement <TOOL>")]
        [Description("The tool to use instead.")]
        public string Replacement { get; init; } = string.Empty;

        [CommandOption("--reason <WHY>")]
        [Description("Why, where nothing replaces it.")]
        public string Reason { get; init; } = string.Empty;
    }

    /// <inheritdoc />
    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        if (settings.DryRun)
        {
            return ToolLifecycleOutput.DryRun(output, settings.Tool, "deprecated");
        }

        var done = _registry.Deprecate(
            settings.Tool,
            settings.Replacement is { Length: > 0 } instead ? instead : null,
            settings.Reason is { Length: > 0 } why ? why : null);

        return done.Failed ? output.Fail(done) : ToolLifecycleOutput.Done(output, settings.Tool, ToolLifecycle.Deprecated);
    }
}

/// <summary>Retiring a deprecated tool.</summary>
[Description("Retire a deprecated shared tool nobody has used for thirty days.")]
[CommandMeta(CommandCategory.Start,
    Intent = "tools retire remove lifecycle unused", Mutates = true,
    Example = "loadout tools retire free-disk-by-cache")]
public sealed class ToolRetireCommand : Command<ToolRetireCommand.Settings>
{
    private readonly IToolRegistry _registry;
    private readonly IAnsiConsole _console;

    public ToolRetireCommand(IToolRegistry registry, IAnsiConsole console)
    {
        _registry = registry;
        _console = console;
    }

    public sealed class Settings : ToolSettings
    {
        [CommandArgument(0, "<TOOL>")]
        [Description("The tool.")]
        public string Tool { get; init; } = string.Empty;
    }

    /// <inheritdoc />
    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        if (settings.DryRun)
        {
            return ToolLifecycleOutput.DryRun(output, settings.Tool, "retired");
        }

        var done = _registry.Retire(settings.Tool);

        return done.Failed ? output.Fail(done) : ToolLifecycleOutput.Done(output, settings.Tool, ToolLifecycle.Retired);
    }
}

/// <summary>What deprecate and retire say.</summary>
internal static class ToolLifecycleOutput
{
    public static int DryRun(CommandOutput output, string tool, string would)
    {
        if (output.IsJson)
        {
            output.WriteJson(new { dry_run = true, tool });
        }
        else
        {
            output.WriteLine($"[dim]Dry run: nothing was changed.[/] {Markup.Escape(tool)} would be {would}.");
        }

        return CommandOutput.Success();
    }

    public static int Done(CommandOutput output, string tool, string lifecycle)
    {
        if (output.IsJson)
        {
            output.WriteJson(new { tool, lifecycle });
        }
        else
        {
            output.WriteLine($"[green]{Markup.Escape(tool)} is {Markup.Escape(lifecycle)}.[/]");
        }

        return CommandOutput.Success();
    }
}

/// <summary>
/// Saying that a remediator may run one version of a shared tool on this
/// machine.
/// </summary>
/// <remarks>
/// The key an agent can never turn, as for a team's remedies: it is kept in
/// this machine's configuration, against the exact script, and a changed
/// script is asked about again.
/// </remarks>
[Description("Trust one version of a shared tool so a remediator may run it, or take that back.")]
[CommandMeta(CommandCategory.Start,
    Intent = "tools trust approve allow script version remediator", Mutates = true,
    Example = "loadout tools trust free-disk-by-cache@1.2")]
public sealed class ToolTrustCommand : AsyncCommand<ToolTrustCommand.Settings>
{
    private readonly IToolRegistry _registry;
    private readonly IConfigurationService _configuration;
    private readonly IAnsiConsole _console;

    public ToolTrustCommand(IToolRegistry registry, IConfigurationService configuration, IAnsiConsole console)
    {
        _registry = registry;
        _configuration = configuration;
        _console = console;
    }

    public sealed class Settings : ToolSettings
    {
        [CommandArgument(0, "<TOOL>")]
        [Description("The tool and version, as name@version.")]
        public string Tool { get; init; } = string.Empty;

        [CommandOption("--revoke")]
        [Description("Take the agreement back.")]
        public bool Revoke { get; init; }

        [CommandOption("--by <WHO>")]
        [Description("Who is saying so, for the record. Your user name when omitted.")]
        public string By { get; init; } = string.Empty;
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);
        var (name, version) = ToolShapes.Split(settings.Tool);

        if (version is not { Length: > 0 })
        {
            return output.Fail("Trust is given to one version: name it as name@version.", ExitCode.InvalidArguments);
        }

        var machine = await _configuration.LoadMachineAsync(cancellationToken).ConfigureAwait(false);

        if (machine.Failed)
        {
            return output.Fail(machine);
        }

        var config = machine.Value!;
        var already = config.Teams.TrustedTools.FirstOrDefault(one =>
            string.Equals(one.Tool, name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(one.Version, version, StringComparison.Ordinal));

        string? fingerprint = null;

        if (!settings.Revoke)
        {
            // Only a version whose files still match what was promoted. A
            // person agreeing to a script that has been changed since would be
            // agreeing to something the gate never saw.
            if (_registry.ScriptOf(name, version) is not { } script)
            {
                return output.Fail(
                    $"{name}@{version} is not a promoted version whose files still match, so there is nothing to agree to.",
                    ExitCode.PolicyViolation);
            }

            fingerprint = RemedyCeiling.Fingerprint(script);
        }

        if (settings.DryRun)
        {
            if (output.IsJson)
            {
                output.WriteJson(new { dry_run = true, tool = name, version, revoke = settings.Revoke });
            }
            else
            {
                output.WriteLine(
                    $"[dim]Dry run: nothing was changed.[/] {Markup.Escape(settings.Tool)} would "
                    + (settings.Revoke ? "stop being agreed to." : "be agreed to."));
            }

            return CommandOutput.Success();
        }

        if (already is not null)
        {
            config.Teams.TrustedTools.Remove(already);
        }

        if (!settings.Revoke)
        {
            config.Teams.TrustedTools.Add(new TrustedTool
            {
                Tool = name,
                Version = version,
                Fingerprint = fingerprint!,
                By = settings.By is { Length: > 0 } who ? who : Environment.UserName,
                At = DateTimeOffset.UtcNow,
            });
        }

        var saved = await _configuration.SaveMachineAsync(config, cancellationToken).ConfigureAwait(false);

        if (saved.Failed)
        {
            return output.Fail(saved);
        }

        _registry.RecordTrust(name, version, settings.Revoke, settings.By is { Length: > 0 } by ? by : Environment.UserName);

        if (output.IsJson)
        {
            output.WriteJson(new { tool = name, version, trusted = !settings.Revoke, fingerprint });
        }
        else if (settings.Revoke)
        {
            output.WriteLine($"[yellow]{Markup.Escape(settings.Tool)} is no longer agreed to.[/]");
        }
        else
        {
            output.WriteLine($"[green]{Markup.Escape(settings.Tool)} is agreed to on this machine.[/]");
            output.WriteLine(
                "[dim]A remediator may run it where this machine lets a trusted remedy of its kind run. "
                + "Changing the script takes this back.[/]");
        }

        return CommandOutput.Success();
    }
}
