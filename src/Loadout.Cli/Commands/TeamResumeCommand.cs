using System.ComponentModel;
using Loadout.Agents.Teams;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Instructions;
using Loadout.Core.Projects;
using Loadout.Core.Teams;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Platform.Abstractions;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>Picks up a run that has ended, where it stopped.</summary>
/// <remarks>
/// <para>
/// A run that stopped on its budget, its round limit, a person's stop or an
/// agent's usage limit had the goal half done and no way back to it: starting
/// it again briefed a new lead from nothing, which asked for the same work
/// again and paid for it twice.
/// </para>
/// <para>
/// The run keeps its identifier, directory and journal, so everything that
/// reads it - status, the log, the dashboard - sees one run that stopped and
/// carried on. Its lead resumes its own agent session where the run recorded
/// one, and is otherwise started fresh and told where the run got to.
/// </para>
/// <para>
/// Refused where it would stop again at once: a run that ended on its budget
/// needs more money, and one that ended on its round limit needs more rounds,
/// before it has anything to spend them on.
/// </para>
/// </remarks>
[Description("Pick up a team run that has ended, where it stopped.")]
[CommandMeta(CommandCategory.Start, Intent = "resume continue pick up restart extend ended stopped run team", Mutates = true)]
public sealed class TeamResumeCommand : AsyncCommand<TeamResumeCommand.Settings>
{
    private readonly ITeamRunner _runner;
    private readonly IRunJournal _journal;
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

    public TeamResumeCommand(
        ITeamRunner runner,
        IRunJournal journal,
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
        _journal = journal;
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

    public sealed class Settings : RunSettings
    {
        [CommandOption("--message <TEXT>")]
        [Description("What to tell the lead as it picks up: what to do next, or what changed.")]
        public string? Message { get; init; }

        [CommandOption("--usd <AMOUNT>")]
        [Description("What it may spend in all from now on, in USD. Needed when it stopped on its budget.")]
        public decimal? Usd { get; init; }

        [CommandOption("--rounds <N>")]
        [Description("How many more rounds it may take. Needed when it stopped on its round limit.")]
        public int Rounds { get; init; }

        [CommandOption("--autonomy <MODE>")]
        [Description("manual, supervised or autonomous. Defaults to what the run had.")]
        public string? Autonomy { get; init; }

        [CommandOption("--model <MODEL>")]
        [Description("A model for every node from now on, written as the agent spells it.")]
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

        var (run, error) = RunControlling.Resolve(_journal, settings.Run);

        if (run is null)
        {
            return output.Fail(error!, ExitCode.ProjectNotFound);
        }

        var (resuming, why) = RunResumption.Read(_journal, run);

        if (resuming is null)
        {
            return output.Fail(why!, ExitCode.InvalidArguments);
        }

        var summary = resuming.Summary;

        if (summary.Project is not { Length: > 0 } slug)
        {
            return output.Fail(
                $"Run {run} does not say which project it worked on, so it cannot be picked up. "
                + "Start it again with 'team run'.",
                ExitCode.InvalidArguments);
        }

        var resolution = await ProjectHandle
            .ResolveAsync(_projects, slug, null, cancellationToken)
            .ConfigureAwait(false);

        if (resolution.Failed)
        {
            return output.Fail(resolution);
        }

        var project = resolution.Value!;

        var (catalogue, specialists, _) = await TeamLoading
            .LoadAsync(_teams, _library, _workspace, _projects, new TeamSettings { Project = slug }, cancellationToken)
            .ConfigureAwait(false);

        if (catalogue.Find(summary.Team) is not { } team)
        {
            return output.Fail(
                $"Run {run} was of the team '{summary.Team}', which is not here any more.",
                ExitCode.ProjectNotFound);
        }

        var autonomy = (settings.Autonomy ?? summary.Autonomy).Trim().ToLowerInvariant();

        if (autonomy == "manual" && !settings.AllowsPrompting && !settings.DryRun)
        {
            return output.Fail(
                "Manual mode asks a person at every step, and nobody can answer here. "
                + "Run it from a terminal, or choose --autonomy supervised.",
                ExitCode.TerminalRequired);
        }

        var directory = _journal.DirectoryOf(run);

        var (cap, rounds, refused) = Settle(
            summary, RunControl.Budget(directory) ?? team.Rules.Budget.Usd, settings.Usd, settings.Rounds);

        if (refused is not null)
        {
            return output.Fail(refused, ExitCode.InvalidArguments);
        }

        var spent = summary.CostUsd;

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
            slug,
            team,
            specialists,
            summary.Goal,
            autonomy,
            settings.DryRun,
            settings.Agent,
            rounds,
            settings.Offline,
            settings.NoSync,
            settings.Model,
            OutwardAllowed: ceiling.Allowed,
            Remediation: machine.Value?.Teams.Remediation,
            TrustedRemedies: machine.Value?.Teams.TrustedRemedies,
            Resuming: run,
            ResumeMessage: settings.Message is { Length: > 0 } said ? said.Trim() : null,

            // The team's rule. A wait the run was started with on the command
            // line is not carried over: the journal records it for reading, not
            // as a setting, and a resume that quietly reapplied an old flag
            // would be one somebody had not asked for this time.
            TakeRecommendationAfter: Loadout.Models.Teams.TeamDuration.Parse(team.Rules.TakeRecommendationAfter));

        if (!settings.DryRun && settings.Usd is { } raised)
        {
            await RunControl.SetBudgetAsync(directory, raised, cancellationToken).ConfigureAwait(false);
        }

        if (!output.IsJson)
        {
            output.WriteLine(
                $"[bold]Picking up {Markup.Escape(run)}[/] [dim]({Markup.Escape(team.Name)}; it ended: "
                + $"{Markup.Escape(summary.Ended ?? "without saying why")})[/]");

            output.WriteLine(
                resuming.LeadSession is { Length: > 0 }
                    ? "[dim]The lead resumes its own session.[/]"
                    : "[dim]This run recorded no session for its lead, so a new one is told where it got to.[/]");

            output.WriteLine(
                $"[dim]Spent so far ${spent:0.00}"
                + (cap is { } shown ? $" of ${shown:0.00}" : string.Empty)
                + $", {summary.Rounds} round(s)"
                + (rounds > 0 ? $" of {rounds}" : string.Empty)
                + ".[/]");
        }

        return await TeamRunCommand.DriveAsync(
            _runner, _console, _reading, _git, _paths, _processes,
            output, settings, request, project, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// What a picked-up run may spend and how many rounds it may take, or why
    /// it would stop again at once.
    /// </summary>
    /// <remarks>
    /// Money first: picked up without any, a run that stopped on its budget
    /// would stop on it again before its lead said a word. Rounds are counted
    /// from where it got to, so "--rounds 3" means three more.
    /// </remarks>
    internal static (decimal? Budget, int Rounds, string? Refused) Settle(
        RunSummary summary,
        decimal? budget,
        decimal? usd,
        int moreRounds)
    {
        ArgumentNullException.ThrowIfNull(summary);

        var spent = summary.CostUsd;

        if (usd is { } asked)
        {
            if (asked <= spent)
            {
                return (null, 0,
                    $"It has already spent ${spent:0.00}, so a budget of ${asked:0.00} would stop it at once. "
                    + "Give it more than it has spent.");
            }

            budget = asked;
        }
        else if (budget is { } limit && spent >= limit)
        {
            return (null, 0,
                $"It has spent ${spent:0.00} of its ${limit:0.00} budget, so it would stop again at once. "
                + "Give it more with --usd.");
        }

        if (moreRounds > 0)
        {
            return (budget, summary.Rounds + moreRounds, null);
        }

        if (summary.RoundLimit > 0 && summary.Rounds >= summary.RoundLimit)
        {
            return (null, 0,
                $"It has taken all {summary.RoundLimit} of its rounds, so it would stop again at once. "
                + "Give it more with --rounds.");
        }

        return (budget, summary.RoundLimit, null);
    }
}
