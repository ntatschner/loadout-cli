using System.ComponentModel;
using Loadout.Agents.Teams;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Instructions;
using Loadout.Core.Projects;
using Loadout.Core.Teams;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Models.Instructions;
using Loadout.Models.Teams;
using Loadout.Platform.Abstractions;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>Shared settings for the team commands.</summary>
public class TeamSettings : GlobalSettings
{
    [CommandOption("--project <SLUG>")]
    [Description("Project the team works on. Defaults to the repository you are in.")]
    public string? Project { get; init; }
}

/// <summary>What the team commands share: loading the catalogue for a project, or for none.</summary>
internal static class TeamLoading
{
    /// <summary>
    /// Where a team came from, for the end of a line, and nothing at all for
    /// the ones that ship.
    /// </summary>
    /// <remarks>
    /// The one worth saying is the pack: it is somebody else's repository, read
    /// once and pinned at a commit, and a team file says which roles run with
    /// which permissions. The built-ins are the default, and a row that says so
    /// on every line teaches people to skim the lines that differ.
    /// </remarks>
    internal static string Whose(SpecialistOrigin origin) => origin switch
    {
        SpecialistOrigin.Pack => ", from a pack",
        SpecialistOrigin.Workspace => ", from your workspace",
        SpecialistOrigin.Project => ", from this project",
        _ => string.Empty,
    };

    /// <summary>
    /// The teams and the library they draw on, for the project named or the
    /// one this directory is in. When neither resolves, the built-ins and the
    /// workspace's own still load, so a listing works from anywhere; a run
    /// needs a project and says so itself.
    /// </summary>
    public static async Task<(TeamCatalogueResult Teams, SpecialistCatalogue Specialists, string? Slug)> LoadAsync(
        ITeamCatalogue teams,
        ISpecialistLibrary library,
        IWorkspaceManager workspace,
        IProjectService projects,
        TeamSettings settings,
        CancellationToken ct)
    {
        var root = workspace.IsAvailable() ? workspace.LocalPath : null;

        var resolution = await ProjectHandle.ResolveAsync(projects, settings.Project, settings.Repo, ct)
            .ConfigureAwait(false);

        var slug = resolution.Succeeded ? resolution.Value!.Entry.Slug : null;

        var specialists = await library.LoadAsync(root, slug, ct).ConfigureAwait(false);
        var catalogue = await teams.LoadAsync(root, slug, specialists, ct).ConfigureAwait(false);

        return (catalogue, specialists, slug);
    }
}

/// <summary>The teams that can be run, from the launcher, the workspace and the project.</summary>
[Description("List the teams that can be run here, and what is wrong with any of them.")]
[CommandMeta(CommandCategory.Start,
    Intent = "teams agents orchestration swarm crew multi-agent list catalogue")]
public sealed class TeamListCommand : AsyncCommand<TeamSettings>
{
    private readonly ITeamCatalogue _teams;
    private readonly ISpecialistLibrary _library;
    private readonly IWorkspaceManager _workspace;
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;

    public TeamListCommand(
        ITeamCatalogue teams,
        ISpecialistLibrary library,
        IWorkspaceManager workspace,
        IProjectService projects,
        IAnsiConsole console)
    {
        _teams = teams;
        _library = library;
        _workspace = workspace;
        _projects = projects;
        _console = console;
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        TeamSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        var (catalogue, _, slug) = await TeamLoading
            .LoadAsync(_teams, _library, _workspace, _projects, settings, cancellationToken)
            .ConfigureAwait(false);

        var teams = catalogue.Teams.Values.OrderBy(t => t.Name, StringComparer.Ordinal).ToList();

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                project = slug,
                teams = teams.Select(t => new
                {
                    t.Name,
                    t.Description,
                    t.Template,
                    t.Lead,
                    nodes = t.Nodes.Count,
                    autonomy = t.Rules.Autonomy,
                    origin = catalogue.Origin(t.Name).ToString().ToLowerInvariant(),
                }),
                findings = catalogue.Findings.Select(f => new { team = f.Rule, f.Kind, f.Detail }),
            });

            return CommandOutput.Success();
        }

        foreach (var team in teams)
        {
            output.WriteLine(
                $"[bold]{Markup.Escape(team.Name)}[/]"
                + (team.Template ? "  [dim]template[/]" : string.Empty)
                + $"  [dim]{team.Nodes.Count} node(s), {Markup.Escape(team.Rules.Autonomy)}"
                + $"{TeamLoading.Whose(catalogue.Origin(team.Name))}[/]");
            output.WriteLine($"  {Markup.Escape(team.Description)}");
        }

        if (catalogue.Findings.Count > 0)
        {
            output.WriteBlankLine();
            output.WriteLine("[bold]What would stop a run[/]");

            foreach (var finding in catalogue.Findings)
            {
                output.WriteLine($"  [yellow]{Markup.Escape(finding.Rule ?? "?")}[/] {Markup.Escape(finding.Detail)}");
            }
        }

        output.WriteBlankLine();
        output.WriteLine("[dim]Run one with:[/] loadout team run <team> \"<goal>\"");

        return CommandOutput.Success();
    }
}

/// <summary>One team in full: its nodes, its rules, and whether it could run.</summary>
[Description("Show a team: its nodes, their roles, and the rules a run follows.")]
[CommandMeta(CommandCategory.Start, Intent = "team show nodes roles wiring rules budget")]
public sealed class TeamShowCommand : AsyncCommand<TeamShowCommand.Settings>
{
    private readonly ITeamCatalogue _teams;
    private readonly ISpecialistLibrary _library;
    private readonly IWorkspaceManager _workspace;
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;

    private readonly Loadout.Core.Configuration.IConfigurationService _configuration;

    public TeamShowCommand(
        ITeamCatalogue teams,
        ISpecialistLibrary library,
        IWorkspaceManager workspace,
        IProjectService projects,
        Loadout.Core.Configuration.IConfigurationService configuration,
        IAnsiConsole console)
    {
        _configuration = configuration;
        _teams = teams;
        _library = library;
        _workspace = workspace;
        _projects = projects;
        _console = console;
    }

    public sealed class Settings : TeamSettings
    {
        [CommandArgument(0, "<TEAM>")]
        [Description("The team's name, as 'team list' shows it.")]
        public string Team { get; init; } = string.Empty;
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        var (catalogue, specialists, _) = await TeamLoading
            .LoadAsync(_teams, _library, _workspace, _projects, settings, cancellationToken)
            .ConfigureAwait(false);

        var team = catalogue.Find(settings.Team);

        if (team is null)
        {
            return output.Fail(
                $"No team named '{settings.Team}'. See what there is with: loadout team list",
                ExitCode.ProjectNotFound);
        }

        var problems = catalogue.Findings.Where(f => string.Equals(f.Rule, team.Name, StringComparison.OrdinalIgnoreCase)).ToList();

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                team.Name,
                team.Description,
                team.Template,
                team.Lead,
                nodes = team.Nodes.Select(n => new
                {
                    name = n.Key,
                    n.Value.Role,
                    mode = specialists.Find(n.Value.Role)?.Role?.Mode,
                    deliverable = specialists.Find(n.Value.Role)?.Role?.Deliverable,
                    agent = n.Value.Agent.Length > 0 ? n.Value.Agent : null,
                    model = n.Value.Model.Length > 0 ? n.Value.Model : null,
                    n.Value.Delegates,
                    n.Value.Worktree,
                    n.Value.Parallel,
                }),
                rules = new
                {
                    team.Rules.Autonomy,
                    budget = new { team.Rules.Budget.Usd, team.Rules.Budget.TurnsPerNode, team.Rules.Budget.WallClock },
                    gates = new { team.Rules.Gates.Outward, team.Rules.Gates.Merge, team.Rules.Gates.OutwardAllowedWhenAutonomous },
                    team.Rules.StopWhen,
                },
                findings = problems.Select(f => new { f.Kind, f.Detail }),
            });

            return CommandOutput.Success();
        }

        output.WriteLine($"[bold]{Markup.Escape(team.Name)}[/]" + (team.Template ? "  [dim]template: copy it, do not run it[/]" : string.Empty));
        output.WriteLine($"  {Markup.Escape(team.Description)}");
        output.WriteBlankLine();

        foreach (var (name, node) in team.Nodes)
        {
            var role = specialists.Find(node.Role);
            var mark = name == team.Lead ? "lead" : string.Empty;

            output.WriteLine(
                $"  {Markup.Escape(name),-20} {Markup.Escape(node.Role),-24} "
                + $"[dim]{Markup.Escape(role?.Role?.Mode ?? "?"),-12} {Markup.Escape(role?.Role?.Deliverable ?? "?"),-9}[/]"
                + (node.Parallel > 1 ? $" x{node.Parallel}" : string.Empty)
                + (node.Worktree ? " worktree" : string.Empty)
                + (node.Model is { Length: > 0 } model ? $" [dim]{Markup.Escape(model)}[/]" : string.Empty)
                + (mark.Length > 0 ? $" [bold]{mark}[/]" : string.Empty));

            if (node.Delegates.Count > 0)
            {
                output.WriteLine($"  {string.Empty,-20} [dim]may request: {Markup.Escape(string.Join(", ", node.Delegates))}[/]");
            }
        }

        output.WriteBlankLine();
        output.WriteLine($"  autonomy   {Markup.Escape(team.Rules.Autonomy)}");
        output.WriteLine(
            $"  budget     {(team.Rules.Budget.Usd is { } usd ? $"${usd:0.##}" : "no cap")}, "
            + $"{(team.Rules.Budget.TurnsPerNode is { } turns ? $"{turns} turns per node" : "the agent's default turns")}"
            + (team.Rules.Budget.WallClock is { Length: > 0 } clock ? $", {Markup.Escape(clock)}" : string.Empty));
        output.WriteLine($"  merge      {(team.Rules.Gates.Merge.Count > 0 ? Markup.Escape(string.Join(" and ", team.Rules.Gates.Merge)) : "no merge gate")}");
        output.WriteLine($"  stops when {Markup.Escape(string.Join(", ", team.Rules.StopWhen))}");

        if (team.Rules.Gates.OutwardAllowedWhenAutonomous.Count > 0)
        {
            output.WriteLine($"  outward, autonomous only: {Markup.Escape(string.Join("; ", team.Rules.Gates.OutwardAllowedWhenAutonomous))}");

            // What the team asks for, then what this machine says to it. A list
            // shown without the answer reads as a grant, which is the one thing
            // a team file can never be.
            var machine = await _configuration.LoadMachineAsync(cancellationToken).ConfigureAwait(false);

            var ceiling = TeamCeiling.Decide(
                team.Rules.Gates.OutwardAllowedWhenAutonomous,
                machine.Value?.Teams.OutwardAllowed);

            output.WriteLine(ceiling.IsShort
                ? $"  [yellow]this machine allows none of that[/]: {Markup.Escape(string.Join("; ", ceiling.Refused))}. "
                    + "An autonomous run is refused until 'loadout config set team-outward-allowed' agrees to it."
                : "  this machine allows all of it in an autonomous run");
        }

        foreach (var finding in problems)
        {
            output.WriteLine($"  [yellow]{Markup.Escape(finding.Detail)}[/]");
        }

        return CommandOutput.Success();
    }
}

/// <summary>What the runs on this machine did.</summary>
[Description("List the team runs on this machine, newest first.")]
[CommandMeta(CommandCategory.Start, Intent = "team runs history what did the team do")]
public sealed class TeamRunsCommand : AsyncCommand<GlobalSettings>
{
    private readonly IRunJournal _journal;
    private readonly IAnsiConsole _console;

    public TeamRunsCommand(IRunJournal journal, IAnsiConsole console)
    {
        _journal = journal;
        _console = console;
    }

    /// <inheritdoc />
    protected override Task<int> ExecuteAsync(
        CommandContext context,
        GlobalSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        var runs = _journal.List()
            .Select(_journal.Summarise)
            .Where(result => result.Succeeded)
            .Select(result => result.Value!)
            .ToList();

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                runs = runs.Select(run => new
                {
                    run = run.RunId,
                    run.Team,
                    run.Project,
                    run.Autonomy,
                    run.Goal,
                    ended = run.Ended,
                    running = run.Running,
                    cost = run.CostUsd,
                    run.Rounds,
                    nodes = run.Nodes.Count,
                    run.Merged,
                }),
            });

            return Task.FromResult(CommandOutput.Success());
        }

        if (runs.Count == 0)
        {
            output.WriteLine("[dim]No team has run on this machine yet.[/]");
            output.WriteLine("[dim]Start one with:[/] loadout team run <team> \"<goal>\"");

            return Task.FromResult(CommandOutput.Success());
        }

        foreach (var run in runs)
        {
            output.WriteLine(
                $"{Markup.Escape(run.RunId),-22} {Markup.Escape(run.Team),-20} "
                + $"[dim]{Markup.Escape(run.Project ?? string.Empty),-16}[/] "
                + (run.WaitingForYou
                    ? "[yellow]waiting for you[/]"
                    : run.Running ? "[yellow]running[/]" : $"[dim]{Markup.Escape(run.Ended ?? "ended")}[/]")
                + $"  [dim]${run.CostUsd:0.00}[/]");

            if (run.Goal is { Length: > 0 })
            {
                output.WriteLine($"  [dim]{Markup.Escape(Shorten(run.Goal))}[/]");
            }
        }

        output.WriteBlankLine();
        output.WriteLine("[dim]One in full with:[/] loadout team status [<run>]");

        return Task.FromResult(CommandOutput.Success());
    }

    private static string Shorten(string text) => text.Length > 90 ? text[..90] + "…" : text;
}

/// <summary>Where one run got to.</summary>
[Description("Show one team run: what each node is doing now, where it got to, what it cost, and what it left behind.")]
[CommandMeta(CommandCategory.Start, Intent = "team status run progress nodes cost what is happening")]
public sealed class TeamStatusCommand : AsyncCommand<TeamStatusCommand.Settings>
{
    private readonly IRunJournal _journal;
    private readonly IAnsiConsole _console;
    private readonly TimeProvider _time;

    public TeamStatusCommand(IRunJournal journal, IAnsiConsole console, TimeProvider time)
    {
        _journal = journal;
        _console = console;
        _time = time;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "[RUN]")]
        [Description("The run, as 'team runs' shows it. The most recent one when omitted.")]
        public string? Run { get; init; }
    }

    /// <inheritdoc />
    protected override Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        var runId = settings.Run ?? _journal.List(1).FirstOrDefault();

        if (runId is null)
        {
            return Task.FromResult(output.Fail(
                "No team has run on this machine yet. Start one with: loadout team run <team> \"<goal>\"",
                ExitCode.ProjectNotFound));
        }

        var read = _journal.Summarise(runId);

        if (read.Failed)
        {
            return Task.FromResult(output.Fail(read));
        }

        var run = read.Value!;
        var quiet = _time.GetUtcNow() - run.LastSeen;

        // What the run produced, which this command has always said it shows.
        // It is read from the reports the nodes already wrote and the journal
        // already has; nothing new is recorded to make this work.
        var events = _journal.Read(run.RunId);
        var behind = RunLeftBehind.In(run.Directory, events.Failed ? [] : events.Value!);

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                run = run.RunId,
                run.Team,
                run.Goal,
                run.Project,
                run.Path,
                run.Autonomy,
                run.Directory,
                started = run.Started,
                finished = run.Finished,
                ended = run.Ended,
                running = run.Running,
                quietSeconds = (int)quiet.TotalSeconds,
                cost = run.CostUsd,
                run.Rounds,
                roundLimit = run.RoundLimit,
                elapsedSeconds = (int)run.Elapsed.TotalSeconds,
                atMostRemainingSeconds = run.AtMostRemaining is { } remaining ? (int)remaining.TotalSeconds : (int?)null,
                nodes = run.Nodes.Select(node => new
                {
                    node.Node,
                    node.Role,
                    node.State,
                    node.Turns,
                    cost = node.CostUsd,
                    node.Branch,
                    node.Denials,
                    node.Doing,
                    node.Said,
                    node.Trouble,
                    startedAt = node.Started,
                    tookSeconds = node.Took is { } took ? (int)took.TotalSeconds : (int?)null,
                    lastSeen = node.LastSeen,
                }),
                run.Branches,
                run.Merged,
                delivered = behind.Delivered.Select(one => new
                {
                    one.Node,
                    one.Kind,
                    at = one.Ref,
                    one.Note,
                }),
                evidence = behind.Evidence.Select(one => new
                {
                    one.Node,
                    one.Kind,
                    at = one.Ref,
                    one.Result,
                    one.Note,
                }),
                decisions = behind.Decisions.Select(one => new
                {
                    one.At,
                    one.What,
                    one.Outcome,
                    one.Detail,
                }),
                unreadableReports = behind.Unreadable,
            });

            return Task.FromResult(CommandOutput.Success());
        }

        output.WriteLine(
            $"[bold]{Markup.Escape(run.RunId)}[/]  {Markup.Escape(run.Team)}  "
            + (run.Project is { Length: > 0 } on ? $"{Markup.Escape(on)}  " : string.Empty)
            + $"{Markup.Escape(run.Autonomy)}  "
            + (run.WaitingForYou
                ? "[yellow]waiting for you[/]"
                : run.Running
                    ? $"[yellow]running[/]  [dim]{Elapsed(quiet)} since it last said anything[/]"
                    : $"[dim]{Markup.Escape(run.Ended ?? "ended")}[/]")
            + $"  [dim]{Rounds(run)}, {Elapsed(run.Elapsed)} so far, ${run.CostUsd:0.00}[/]");

        // Everything wanting attention that is not a question - the
        // questions get their own block below, with how to answer them.
        foreach (var reason in RunAttention.For(run, _time.GetUtcNow())
            .Where(reason => reason.Kind != AttentionKind.Asking))
        {
            output.WriteBlankLine();
            output.WriteLine($"  [yellow]needs you[/] {Markup.Escape(reason.Detail)}");
            output.WriteLine($"    [dim]clears when {Markup.Escape(reason.Clears)}[/]");
        }

        // What it has stopped to ask, and how to answer it. The same questions
        // the dashboard shows, because they are the same files.
        foreach (var gate in run.Waiting)
        {
            output.WriteBlankLine();
            output.WriteLine($"  [yellow]waiting[/] {Markup.Escape(gate.Asking)}");
            output.WriteLine(
                $"    [dim]loadout team gate {Markup.Escape(run.RunId)} --gate {Markup.Escape(gate.Id)} "
                + $"--answer {Markup.Escape(gate.Choices[0])}[/]");
        }

        if (run.Goal is { Length: > 0 })
        {
            output.WriteLine($"  {Markup.Escape(run.Goal)}");
        }

        if (run.AtMostRemaining is { } left)
        {
            // A ceiling, said as one. At this rate and using every round it
            // has left is the only claim the journal supports, and a bare
            // "about 6m" would be read as a prediction.
            output.WriteLine(
                $"  [dim]at this rate, up to {Elapsed(left)} more if it uses all "
                + $"{run.RoundLimit} rounds[/]");
        }

        output.WriteBlankLine();

        foreach (var node in run.Nodes)
        {
            output.WriteLine(
                $"  {Markup.Escape(node.Node),-16} {Markup.Escape(node.Role),-22} "
                + $"{State(node.State)} [dim]{node.Turns,3} exchange(s)  ${node.CostUsd,6:0.00}[/]"
                + (node.Took is { } took ? $"  [dim]{Elapsed(took)}[/]" : string.Empty)
                + (node.Denials > 0 ? $"  [yellow]{node.Denials} denial(s)[/]" : string.Empty));

            if (node.Doing is { Length: > 0 } doing)
            {
                output.WriteLine($"  {string.Empty,-16} [dim]{Markup.Escape(doing)}[/]");
            }

            // Under it, not instead of it, and marked as the node's own words.
            // The two disagree sometimes and that is the useful part: a node
            // saying it is writing tests while every call it makes is a read
            // is the shape of one that has lost the thread.
            if (node.Said is { Length: > 0 } said)
            {
                output.WriteLine($"  {string.Empty,-16} [dim]says: {Markup.Escape(said)}[/]");
            }

            // What its process said on the way out. Without this a run that
            // failed at launch showed a node, a state and nothing else, and
            // the one line explaining the whole run - a rejected schema, a
            // missing binary - sat in the journal for somebody to find.
            if (node.Trouble is { Length: > 0 } trouble)
            {
                output.WriteLine($"  {string.Empty,-16} [red]{Markup.Escape(Short(trouble))}[/]");
            }

            if (node.Branch is { Length: > 0 } branch)
            {
                var taken = run.Merged.Contains(branch, StringComparer.Ordinal);

                output.WriteLine(
                    $"  {string.Empty,-16} [dim]{Markup.Escape(branch)}[/]"
                    + (taken ? "  [green]merged[/]" : "  [dim]not merged[/]"));
            }
        }

        /*
            What it left behind.

            The reports have carried this from the first run - what each node
            produced and what it showed for it - and nothing read any of it
            back. A run was a list of nodes, a state each and a cost, and
            answering "what did it actually do" meant opening the journal by
            hand.
        */
        if (behind.Delivered.Count > 0)
        {
            output.WriteBlankLine();
            output.WriteLine("[bold]delivered[/]");

            foreach (var one in behind.Delivered)
            {
                output.WriteLine(
                    $"  {Markup.Escape(one.Kind),-9} {Markup.Escape(one.Ref)}"
                    + $"  [dim]{Markup.Escape(one.Node)}[/]");

                if (one.Note is { Length: > 0 } note)
                {
                    output.WriteLine($"  {string.Empty,-9} [dim]{Markup.Escape(note)}[/]");
                }
            }
        }

        /*
            Counted per node, and only the failures written out.

            A run of any size has dozens of these - one had fifty-nine - and
            printing them all buries the single one that did not pass under
            thirty that did. The count says the work was checked; the failure
            is the part somebody has to read. Every one of them is still in
            --json, whole and untruncated.
        */
        if (behind.Evidence.Count > 0)
        {
            output.WriteBlankLine();
            output.WriteLine("[bold]shown for it[/]");

            foreach (var node in behind.Evidence.GroupBy(one => one.Node, StringComparer.Ordinal))
            {
                var passed = node.Count(one => string.Equals(one.Result, "pass", StringComparison.Ordinal));
                var failed = node.Where(one => string.Equals(one.Result, "fail", StringComparison.Ordinal)).ToList();
                var neither = node.Count() - passed - failed.Count;

                var counted = new List<string>();

                if (passed > 0) { counted.Add($"{passed} passed"); }
                if (failed.Count > 0) { counted.Add($"[red]{failed.Count} failed[/]"); }
                if (neither > 0) { counted.Add($"{neither} not run"); }

                output.WriteLine(
                    $"  {Markup.Escape(node.Key),-16} [dim]{string.Join(", ", counted)}[/]");

                foreach (var one in failed)
                {
                    output.WriteLine(
                        $"  {string.Empty,-16} [red]failed[/] {Markup.Escape(one.Kind)}: "
                        + Markup.Escape(Short(one.Ref)));
                }
            }
        }

        if (behind.Decisions.Count > 0)
        {
            output.WriteBlankLine();
            output.WriteLine("[bold]decided[/]");

            foreach (var one in behind.Decisions)
            {
                output.WriteLine(
                    $"  [dim]{one.At.ToLocalTime():HH:mm:ss}[/]  {Markup.Escape(one.Outcome),-11} "
                    + Markup.Escape(Short(one.What))
                    + (one.Detail is { Length: > 0 } why ? $"  [dim]{Markup.Escape(Short(why))}[/]" : string.Empty));
            }
        }

        // Named rather than skipped: a node whose report will not parse is a
        // node whose work nobody can see, and a shorter list looks the same as
        // a quieter run.
        foreach (var one in behind.Unreadable)
        {
            output.WriteBlankLine();
            output.WriteLine($"  [yellow]unreadable[/] {Markup.Escape(one)} [dim]could not be read back[/]");
        }

        output.WriteBlankLine();
        output.WriteLine($"[dim]journal: {Markup.Escape(Path.Combine(run.Directory, "journal.jsonl"))}[/]");
        output.WriteLine($"[dim]Read it with:[/] loadout team log {Markup.Escape(run.RunId)}");

        return Task.FromResult(CommandOutput.Success());
    }

    /// <summary>
    /// Enough of a reference to recognise it.
    /// </summary>
    /// <remarks>
    /// A node will happily put a whole command line or a forty-character hash
    /// in here, and a run with fifty pieces of evidence then scrolls off the
    /// top. The full text is in the report, and --json gives it back whole.
    /// </remarks>
    private static string Short(string text) =>
        text.Length <= 72 ? text : text[..69] + "...";

    /// <summary>
    /// A node's state, coloured, and padded to a column.
    /// </summary>
    /// <remarks>
    /// The padding is applied to the word and the colour wrapped round it
    /// afterwards, rather than the other way about. Padding the marked-up
    /// string counts the tags: "[red]failed[/]" is eighteen characters of
    /// which six are the word, so a column of states lined up only for
    /// whichever ones happened to carry the same length of markup.
    /// </remarks>
    private static string State(string state)
    {
        var word = state switch
        {
            "needs-decision" => "needs a decision",
            _ => state,
        };

        var padded = Markup.Escape(word).PadRight(17);

        return state switch
        {
            "working" => $"[yellow]{padded}[/]",
            "done" => $"[green]{padded}[/]",
            "failed" => $"[red]{padded}[/]",
            "blocked" or "needs-decision" => $"[yellow]{padded}[/]",
            _ => padded,
        };
    }

    private static string Rounds(RunSummary run) => run.RoundLimit > 0
        ? $"round {run.Rounds} of {run.RoundLimit}"
        : $"{run.Rounds} round(s)";

    private static string Elapsed(TimeSpan span) => span.TotalMinutes < 1
        ? $"{(int)span.TotalSeconds}s"
        : span.TotalHours < 1 ? $"{(int)span.TotalMinutes}m" : $"{(int)span.TotalHours}h {span.Minutes}m";
}

/// <summary>Everything a run wrote down, as it wrote it.</summary>
[Description("Read a team run's journal, one line per thing that happened. --follow watches a run that is still going.")]
[CommandMeta(CommandCategory.Start, Intent = "team log journal follow tail watch run")]
public sealed class TeamLogCommand : AsyncCommand<TeamLogCommand.Settings>
{
    /// <summary>How long to wait before looking at the journal again while following.</summary>
    /// <remarks>
    /// A node's turn takes tens of seconds, so nothing is missed by waiting
    /// half of one, and a tighter loop would spend a core on an idle file.
    /// </remarks>
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(500);

    private readonly IRunJournal _journal;
    private readonly IAnsiConsole _console;

    public TeamLogCommand(IRunJournal journal, IAnsiConsole console)
    {
        _journal = journal;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "[RUN]")]
        [Description("The run, as 'team runs' shows it. The most recent one when omitted.")]
        public string? Run { get; init; }

        [CommandOption("--follow")]
        [Description("Keep reading as the run writes, until it finishes.")]
        public bool Follow { get; init; }
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        var runId = settings.Run ?? _journal.List(1).FirstOrDefault();

        if (runId is null)
        {
            return output.Fail(
                "No team has run on this machine yet. Start one with: loadout team run <team> \"<goal>\"",
                ExitCode.ProjectNotFound);
        }

        var read = _journal.Read(runId);

        if (read.Failed)
        {
            return output.Fail(read);
        }

        if (output.IsJson)
        {
            // Read once, whatever --follow says: a document that never ends
            // is not one anything can parse.
            output.WriteJson(new
            {
                run = runId,
                events = read.Value!.Select(entry => new
                {
                    at = entry.At,
                    node = entry.Node,
                    kind = entry.Kind,
                    line = RunJournal.Describe(entry),
                }),
            });

            return CommandOutput.Success();
        }

        var seen = 0;

        foreach (var entry in read.Value!)
        {
            output.WriteLine(Markup.Escape(RunJournal.Describe(entry)));
            seen++;
        }

        if (!settings.Follow || read.Value!.Any(entry => entry.Kind == "run.finished"))
        {
            return CommandOutput.Success();
        }

        if (!settings.AllowsPrompting)
        {
            // Following is watching, and nobody is. The lines above are the
            // whole answer rather than a wait nothing would end.
            output.WriteLine("[dim]Not following: there is nobody here to watch it.[/]");

            return CommandOutput.Success();
        }

        output.WriteLine("[dim]Following. Ctrl+C to stop.[/]");

        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(Interval, cancellationToken).ConfigureAwait(false);

            var again = _journal.Read(runId);

            if (again.Failed)
            {
                continue;
            }

            foreach (var entry in again.Value!.Skip(seen))
            {
                output.WriteLine(Markup.Escape(RunJournal.Describe(entry)));
                seen++;

                if (entry.Kind == "run.finished")
                {
                    return CommandOutput.Success();
                }
            }
        }

        return CommandOutput.Success();
    }
}

/// <summary>Runs a team against a project.</summary>
/// <remarks>
/// The only place a run starts. The launcher's screen and the dashboard,
/// when they exist, run this command through the same parser rather than
/// starting nodes themselves.
/// </remarks>
[Description("Run a team against a project, with a goal. --dry-run shows the lead's launch and starts nothing.")]
[CommandMeta(CommandCategory.Start,
    Intent = "team run orchestrate agents swarm autonomous goal", Mutates = true, Example = "loadout team run iterating-project \"Add --since to loadout usage\"")]
public sealed class TeamRunCommand : AsyncCommand<TeamRunCommand.Settings>
{
    private readonly ITeamRunner _runner;
    private readonly IPlatformPaths _paths;
    private readonly IProcessInspector _processes;
    private readonly Loadout.Core.Git.IGitManager _git;
    private readonly Loadout.Core.Configuration.IConfigurationService _configuration;
    private readonly ITeamCatalogue _teams;
    private readonly ISpecialistLibrary _library;
    private readonly IWorkspaceManager _workspace;
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;

    private readonly ReadingProfile _reading;

    public TeamRunCommand(
        ITeamRunner runner,
        IPlatformPaths paths,
        IProcessInspector processes,
        Loadout.Core.Git.IGitManager git,
        Loadout.Core.Configuration.IConfigurationService configuration,
        ITeamCatalogue teams,
        ISpecialistLibrary library,
        IWorkspaceManager workspace,
        IProjectService projects,
        IAnsiConsole console,
        ReadingProfile reading)
    {
        _runner = runner;
        _paths = paths;
        _processes = processes;
        _git = git;
        _configuration = configuration;
        _teams = teams;
        _library = library;
        _workspace = workspace;
        _projects = projects;
        _console = console;
        _reading = reading;
    }

    public sealed class Settings : TeamSettings
    {
        [CommandArgument(0, "<TEAM>")]
        [Description("The team to run, as 'team list' shows it.")]
        public string Team { get; init; } = string.Empty;

        [CommandArgument(1, "<GOAL>")]
        [Description("What the run is for, in your words. The lead's whole task.")]
        public string Goal { get; init; } = string.Empty;

        [CommandOption("--autonomy <MODE>")]
        [Description("manual, supervised or autonomous. Defaults to the team's own setting.")]
        public string? Autonomy { get; init; }

        [CommandOption("--rounds <N>")]
        [Description("How many times the lead may come back with more requests. Default 5.")]
        public int Rounds { get; init; } = 5;

        [CommandOption("--model <MODEL>")]
        [Description("A model for every node, overriding the team's and the project's. Written as the agent spells it.")]
        public string? Model { get; init; }
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        var resolution = await ProjectHandle
            .ResolveAsync(_projects, settings.Project, settings.Repo, cancellationToken)
            .ConfigureAwait(false);

        if (resolution.Failed)
        {
            return output.Fail(resolution);
        }

        var project = resolution.Value!;

        var (catalogue, specialists, _) = await TeamLoading
            .LoadAsync(_teams, _library, _workspace, _projects, settings, cancellationToken)
            .ConfigureAwait(false);

        var team = catalogue.Find(settings.Team);

        if (team is null)
        {
            return output.Fail(
                $"No team named '{settings.Team}'. See what there is with: loadout team list",
                ExitCode.ProjectNotFound);
        }

        if (string.IsNullOrWhiteSpace(settings.Goal))
        {
            return output.Fail("A run needs a goal: what the lead is for, in your words.", ExitCode.InvalidArguments);
        }

        var autonomy = (settings.Autonomy ?? team.Rules.Autonomy).Trim().ToLowerInvariant();

        if (autonomy == "manual" && !settings.AllowsPrompting && !settings.DryRun)
        {
            // Manual mode is a person at every gate. Without one there is
            // nobody to hold them, and a run that answered its own gates
            // would be autonomous wearing manual's name.
            return output.Fail(
                "Manual mode asks a person at every step, and nobody can answer here. "
                + "Run it from a terminal, or choose --autonomy supervised.",
                ExitCode.TerminalRequired);
        }

        // What this machine lets a team allow, decided against what the team
        // asked for. Refused up front rather than narrowed quietly: a narrowed
        // run spends money for minutes and then fails at the one step it was
        // started for, reporting that a node could not push rather than that
        // this machine never let it.
        var machine = await _configuration.LoadMachineAsync(cancellationToken).ConfigureAwait(false);

        var ceiling = autonomy == "autonomous"
            ? TeamCeiling.Decide(
                team.Rules.Gates.OutwardAllowedWhenAutonomous,
                machine.Value?.Teams.OutwardAllowed)
            : TeamCeiling.Nothing;

        if (ceiling.IsShort)
        {
            return output.Fail(TeamCeiling.Explain(team.Name, ceiling), ExitCode.PolicyViolation);
        }

        var request = new TeamRunRequest(
            project.Entry.Slug,
            team,
            specialists,
            settings.Goal,
            autonomy,
            settings.DryRun,
            settings.Agent,
            settings.Rounds,
            settings.Offline,
            settings.NoSync,
            settings.Model);

        // Where the run's questions go. A terminal answers its own; a run with
        // nobody at one sends them to the dashboard, if a daemon is serving it.
        // Decided once rather than per question, because two places able to
        // answer one question is a race whose loser leaves a dead prompt.
        ITeamConsole console = settings.AllowsPrompting
            ? new TerminalTeamConsole(_console, settings, _reading)
            : Serving()
                ? new DashboardTeamConsole(TimeProvider.System, line => output.WriteLine($"[dim]{Markup.Escape(line)}[/]"))
                : new TerminalTeamConsole(_console, settings, _reading);

        await SayWhereAsync(output, project, cancellationToken).ConfigureAwait(false);

        var result = await _runner.RunAsync(request, console, cancellationToken).ConfigureAwait(false);

        if (result.Failed)
        {
            return output.Fail(result);
        }

        var outcome = result.Value!;

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                run = outcome.RunId,
                outcome.Directory,
                outcome.Ended,
                outcome.Rounds,
                cost = outcome.CostUsd,
                branches = outcome.Branches,
                merged = outcome.Merged,
                final = outcome.FinalReport,
                plan = outcome.LeadPlan is { } plan ? new { plan.Executable, plan.Arguments, plan.WorkingDirectory } : null,
                outcome.Warnings,
            });

            return CommandOutput.Success();
        }

        if (outcome.LeadPlan is { } lead)
        {
            output.WriteLine($"[bold]Dry run:[/] the lead would be started as");
            output.WriteLine($"  {Markup.Escape(lead.Executable)} {Markup.Escape(string.Join(' ', lead.Arguments))}");
            output.WriteLine($"  in {Markup.Escape(lead.WorkingDirectory)}");
        }
        else
        {
            output.WriteBlankLine();
            output.WriteLine($"[bold]Run {Markup.Escape(outcome.RunId)}[/] ended: {Markup.Escape(outcome.Ended)}");
            output.WriteLine($"  {outcome.Rounds} round(s), ${outcome.CostUsd:0.00}");

            if (outcome.FinalReport is { } final)
            {
                output.WriteBlankLine();
                output.WriteLine($"[bold]The lead's report[/]  [dim]{Markup.Escape(final.Status.ToString().ToLowerInvariant())}[/]");
                output.WriteLine($"  {Markup.Escape(final.Summary)}");

                foreach (var deliverable in final.Deliverables)
                {
                    output.WriteLine($"  {Markup.Escape(deliverable.Kind.ToString().ToLowerInvariant())}: {Markup.Escape(deliverable.Ref)}");
                }

                if (final.Questions is { Count: > 0 } questions)
                {
                    foreach (var question in questions)
                    {
                        output.WriteLine($"  [yellow]?[/] {Markup.Escape(question.Question)} [dim](recommended: {Markup.Escape(question.Recommendation)})[/]");
                    }
                }
            }

            if (outcome.Branches is { Count: > 0 } branches)
            {
                output.WriteBlankLine();
                output.WriteLine("[bold]Branches[/]");

                foreach (var (node, branch) in branches)
                {
                    var taken = outcome.Merged?.Contains(branch) == true;

                    output.WriteLine(
                        $"  {Markup.Escape(branch)}  [dim]{Markup.Escape(node)}[/]"
                        + (taken ? "  [green]merged[/]" : "  [dim]not merged[/]"));
                }
            }

            if (outcome.Directory is { } directory)
            {
                output.WriteLine($"  [dim]journal: {Markup.Escape(Path.Combine(directory, "journal.jsonl"))}[/]");
            }
        }

        foreach (var warning in outcome.Warnings)
        {
            output.WriteLine($"[yellow]{Markup.Escape(warning)}[/]");
        }

        return CommandOutput.Success();
    }

    /// <summary>
    /// Whether a daemon is actually serving a dashboard right now.
    /// </summary>
    /// <remarks>
    /// The note alone is not enough: identifiers are reused, and a machine that
    /// restarted leaves one behind that looks exactly like a daemon. Asking
    /// whether that process is still the one that wrote it is the difference
    /// between sending a question somewhere and sending it nowhere.
    /// </remarks>
    private bool Serving() =>
        Loadout.Core.Teams.Daemon.DaemonNote.Read(_paths) is { } note
        && _processes.IsRunning(note.Pid, note.StartedAt);

    /// <summary>
    /// Says which tree the run will work on, before it spends anything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A run works on the project's registered path, which is not necessarily
    /// where somebody typed the command. The first real run of this feature was
    /// started from one worktree and worked on another, on a different branch,
    /// and neither the person nor the run said so - it cost a lead, a planner,
    /// an implementer, a reviewer and a verifier before anybody noticed the
    /// work had landed somewhere else.
    /// </para>
    /// <para>
    /// The branch is the part that matters. A path somebody half-recognises
    /// reads as right; a branch name they are not on reads as wrong at a
    /// glance, which is the whole point of saying it before rather than after.
    /// </para>
    /// <para>
    /// Best-effort: a repository that cannot be read still runs. This is a
    /// courtesy before an expensive thing, not a gate.
    /// </para>
    /// </remarks>
    private async Task SayWhereAsync(
        CommandOutput output,
        Loadout.Models.Projects.ProjectResolution project,
        CancellationToken ct)
    {
        if (output.IsJson || project.LocalPath is not { Length: > 0 } path)
        {
            return;
        }

        var state = await _git.GetStateAsync(path, ct).ConfigureAwait(false);

        var branch = state.Succeeded && state.Value?.Branch is { Length: > 0 } named
            ? $" on [bold]{Markup.Escape(named)}[/]"
            : string.Empty;

        output.WriteLine(
            $"[dim]Working on[/] {Markup.Escape(project.Entry.Slug)} "
            + $"[dim]at[/] {Markup.Escape(path)}{branch}");

        // Only when it is not where they are. Said every time it would be
        // noise, and noise is what somebody skims past on the run that
        // mattered.
        var here = Directory.GetCurrentDirectory();

        if (Elsewhere(here, path))
        {
            output.WriteLine($"[dim]You are in[/] {Markup.Escape(here)}[dim], which is not where this will run.[/]");
        }
    }

    /// <summary>
    /// Whether the run will happen somewhere other than where the person is.
    /// </summary>
    /// <remarks>
    /// Compared as full paths with any trailing separator removed, because
    /// <c>D:\git\x</c> and <c>D:\git\x\</c> are the same place and saying they
    /// are not would put this warning on every run, which is how a warning
    /// stops being read. Case-insensitively on Windows only, where the file
    /// system is: two paths differing in case are one directory there and two
    /// directories elsewhere.
    /// </remarks>
    internal static bool Elsewhere(string here, string there) =>
        !string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(here)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(there)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>The person at the terminal, as the run sees them.</summary>
    private sealed class TerminalTeamConsole : ITeamConsole
    {
        private const string Stop = "Stop the run";

        private readonly IAnsiConsole _console;
        private readonly GlobalSettings _settings;

        private readonly ReadingProfile _reading;

        public TerminalTeamConsole(IAnsiConsole console, GlobalSettings settings, ReadingProfile reading)
        {
            _console = console;
            _settings = settings;
            _reading = reading;
        }

        /// <summary>
        /// Whether anybody is there. The same answer every other prompt in the
        /// launcher uses, so a run asks exactly where a command would.
        /// </summary>
        public bool CanAsk => _settings.AllowsPrompting;

        public Task<bool> ConfirmAsync(string what, CancellationToken ct = default)
        {
            if (!_settings.AllowsPrompting)
            {
                return Task.FromResult(false);
            }

            return Task.FromResult(_console.Confirm($"{Markup.Escape(what)}?", defaultValue: true));
        }

        public Task<string?> DecideAsync(ReportQuestion question, CancellationToken ct = default)
        {
            if (!_settings.AllowsPrompting)
            {
                _console.MarkupLine(
                    $"[yellow]The lead asked: {Markup.Escape(question.Question)} Nobody can answer here, so the run stops.[/]");

                return Task.FromResult<string?>(null);
            }

            var chosen = _reading.Ask(
                _console,
                $"{question.Question} (the lead recommends: {question.Recommendation})",
                [.. question.Options, Stop],
                option => option);

            return Task.FromResult(chosen == Stop ? null : chosen);
        }

        public void Note(string line) => _console.MarkupLine($"[dim]{Markup.Escape(line)}[/]");
    }
}
