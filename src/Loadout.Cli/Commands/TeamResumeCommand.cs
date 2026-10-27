using System.ComponentModel;
using Loadout.Agents.Teams;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Instructions;
using Loadout.Core.Projects;
using Loadout.Core.Teams;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Models.Teams;
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
        [Description("What it may spend in all from now on, in USD, or none for no cap. Needed when it stopped on its budget.")]
        public string? Usd { get; init; }

        [CommandOption("--rounds <N>")]
        [Description("How many more rounds it may take. Needed when it stopped on its round limit.")]
        public int Rounds { get; init; }

        [CommandOption("--autonomy <MODE>")]
        [Description("manual, supervised or autonomous. Defaults to what the run had.")]
        public string? Autonomy { get; init; }

        [CommandOption("--model <MODEL>")]
        [Description("A model for every node from now on, written as the agent spells it.")]
        public string? Model { get; init; }

        [CommandOption("--answer <TEXT>")]
        [Description("An answer to a question the lead stopped on. Give one per question, in the order they are listed.")]
        public string[] Answer { get; init; } = [];

        [CommandOption("--without-answers")]
        [Description("Carry on without answering the questions the lead stopped on. It is told you chose not to.")]
        public bool WithoutAnswers { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(
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

        // The questions it stopped on, answered before anything is spent or
        // anything else is looked up: refusing them needs only the run. A
        // run judged again takes no lead turn, so there is nobody to tell.
        var open = RunHandovers.Settled(summary) is not null ? [] : resuming.Questions;
        var pairing = RunQuestions.Pair(open, settings.Answer, settings.WithoutAnswers);

        if (pairing.Refusal is { } unpaired)
        {
            return output.Fail(unpaired, pairing.Code);
        }

        var answers = pairing.Answers;

        if (pairing.Ask && settings.DryRun)
        {
            // Changes nothing, so it asks nothing either; it says what it would.
            if (!output.IsJson)
            {
                output.WriteLine("[dim]It would ask you first:[/]");
                output.WriteLine(Markup.Escape(RunQuestions.Listed(open)));
            }
        }
        else if (pairing.Ask)
        {
            if (!settings.AllowsPrompting)
            {
                // Not quietly picked up: run 20261023-1740-2677 was, twice,
                // and its lead found the same questions and nothing new.
                return output.Fail(RunQuestions.Unanswered(run, open), ExitCode.TerminalRequired);
            }

            answers = Ask(open);
        }

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

        // As for a new run: with a daemon serving the dashboard, the page holds the gates.
        if (autonomy == "manual" && !settings.AllowsPrompting && !settings.DryRun && !TeamRunCommand.Serving(_paths, _processes))
        {
            return output.Fail(
                "Manual mode asks a person at every step, and nobody can answer here. "
                + "Run it from a terminal, or choose --autonomy supervised.",
                ExitCode.TerminalRequired);
        }

        var directory = _journal.DirectoryOf(run);

        var machine = await _configuration.LoadMachineAsync(cancellationToken).ConfigureAwait(false);

        if (!UsdCap.TryParse(settings.Usd, out var asked))
        {
            return output.Fail(UsdCap.Refusal(settings.Usd!), ExitCode.InvalidArguments);
        }

        var (cap, rounds, refused) = Settle(
            summary,
            RunControl.Cap(directory).Or(team.Rules.Budget.Cap).Or(TeamRunCommand.MachineBudget(machine.Value)),
            asked,
            settings.Rounds);

        // A run whose handover has every criterion checked is judged again
        // without a lead turn, so it spends nothing and takes no round: what
        // it ran out of does not stop it.
        var rejudging = RunHandovers.Settled(summary) is not null;

        if (rejudging && refused is not null)
        {
            (cap, rounds, refused) = (RunControl.Cap(directory), summary.RoundLimit, null);
        }

        if (refused is not null)
        {
            return output.Fail(refused, ExitCode.InvalidArguments);
        }

        var spent = summary.CostUsd;

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
            TrustedTools: machine.Value?.Teams.TrustedTools,
            Resuming: run,
            ResumeMessage: settings.Message is { Length: > 0 } said ? said.Trim() : null,
            ResumeAnswers: answers,
            WithoutAnswers: settings.WithoutAnswers,

            // The team's rule. A wait the run was started with on the command
            // line is not carried over: the journal records it for reading, not
            // as a setting, and a resume that quietly reapplied an old flag
            // would be one somebody had not asked for this time.
            TakeRecommendationAfter: Loadout.Models.Teams.TeamDuration.Parse(team.Rules.TakeRecommendationAfter),
            OnTimeout: Loadout.Models.Teams.TeamTimeout.Parse(team.Rules.OnTimeout),
            MachineBudget: TeamRunCommand.MachineBudget(machine.Value));

        if (!settings.DryRun && asked.IsSet)
        {
            await RunControl.SetCapAsync(directory, asked, cancellationToken).ConfigureAwait(false);
        }

        if (!output.IsJson)
        {
            output.WriteLine(
                $"[bold]Picking up {Markup.Escape(run)}[/] [dim]({Markup.Escape(team.Name)}; it ended: "
                + $"{Markup.Escape(summary.Ended ?? "without saying why")})[/]");

            output.WriteLine(
                rejudging
                    ? "[dim]Everything it handed over has been checked, so it is judged again without a turn for the lead.[/]"
                    : resuming.LeadSession is { Length: > 0 }
                    ? "[dim]The lead resumes its own session.[/]"
                    : "[dim]This run recorded no session for its lead, so a new one is told where it got to.[/]");

            output.WriteLine(
                $"[dim]Spent so far ${spent:0.00}"
                + (cap.Usd is { } shown ? $" of ${shown:0.00}" : cap.None ? ", with no cap" : string.Empty)
                + $", {summary.Rounds} round(s)"
                + (rounds > 0 ? $" of {rounds}" : string.Empty)
                + ".[/]");

            foreach (var (question, answer) in answers ?? [])
            {
                output.WriteLine($"[dim]Asked:[/] {Markup.Escape(question)}");
                output.WriteLine($"[dim]  You answered:[/] {Markup.Escape(answer)}");
            }

            if (settings.WithoutAnswers && open.Count > 0)
            {
                output.WriteLine($"[dim]Carrying on without answering {open.Count} question(s); the lead is told so.[/]");
            }

            // Before anything is spent, because the budget is only checked
            // between rounds and a long lead's one turn can pass it on its own.
            if (!rejudging && resuming.LikelyCost(spent, cap.Usd) is { } likely)
            {
                output.WriteLine($"[yellow]{Markup.Escape(likely)}[/]");
            }
        }

        return await TeamRunCommand.DriveAsync(
            _runner, _console, _reading, _git, _paths, _processes,
            output, settings, request, project, _configuration, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The term offered for an answer in the person's own words.</summary>
    internal const string SomethingElse = "Something else…";

    /// <summary>
    /// Puts each open question to the person at the terminal: the lead's
    /// options, its recommendation first, or their own words.
    /// </summary>
    private IReadOnlyList<ResumeAnswer> Ask(IReadOnlyList<ReportQuestion> open)
    {
        var answers = new List<ResumeAnswer>();

        _console.MarkupLine($"[bold]The lead stopped on {open.Count} question(s) only you can answer.[/]");

        foreach (var one in open)
        {
            var choices = (one.Options ?? [])
                .OrderBy(option => string.Equals(option, one.Recommendation, StringComparison.Ordinal) ? 0 : 1)
                .Append(SomethingElse)
                .ToList();

            var chosen = _console.Prompt(new SelectionPrompt<string>()
                .Title(Markup.Escape(one.Question) + (one.Recommendation is { Length: > 0 } r ? $" [dim](recommended: {Markup.Escape(r)})[/]" : string.Empty))
                .AddChoices(choices)
                .UseConverter(Markup.Escape));

            if (chosen == SomethingElse)
            {
                chosen = _console.Prompt(new TextPrompt<string>("Your answer:"));
            }

            answers.Add(new ResumeAnswer(one.Question.Trim(), chosen.Trim()));
        }

        return answers;
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
    internal static (UsdCap Budget, int Rounds, string? Refused) Settle(
        RunSummary summary,
        UsdCap budget,
        UsdCap usd,
        int moreRounds)
    {
        ArgumentNullException.ThrowIfNull(summary);

        var spent = summary.CostUsd;

        if (usd.IsSet)
        {
            if (usd.Usd is { } asked && asked <= spent)
            {
                return (UsdCap.Unset, 0,
                    $"It has already spent ${spent:0.00}, so a budget of ${asked:0.00} would stop it at once. "
                    + $"Give it more than it has spent, or '{UsdCap.NoneWord}' for no cap.");
            }

            budget = usd;
        }
        else if (budget.Usd is { } limit && spent >= limit)
        {
            return (UsdCap.Unset, 0,
                $"It has spent ${spent:0.00} of its ${limit:0.00} budget, so it would stop again at once. "
                + $"Give it more with --usd, or --usd {UsdCap.NoneWord} for no cap.");
        }

        if (moreRounds > 0)
        {
            return (budget, summary.Rounds + moreRounds, null);
        }

        if (summary.RoundLimit > 0 && summary.Rounds >= summary.RoundLimit)
        {
            return (UsdCap.Unset, 0,
                $"It has taken all {summary.RoundLimit} of its rounds, so it would stop again at once. "
                + "Give it more with --rounds.");
        }

        return (budget, summary.RoundLimit, null);
    }
}
