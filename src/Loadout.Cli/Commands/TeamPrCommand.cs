using System.ComponentModel;
using System.Globalization;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Git;
using Loadout.Core.Teams;
using Loadout.Models;
using Loadout.Platform.Abstractions;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>Opens a pull request for what one node of a run produced.</summary>
/// <remarks>
/// <para>
/// The end of the line for a node's work: it has a branch, the branch has a
/// diff somebody has read on the dashboard, and the next thing anybody does
/// with it is open a pull request. Doing that by hand means finding the branch
/// name, remembering what the run was for and writing the body again.
/// </para>
/// <para>
/// It pushes the branch first, because a pull request needs it on the remote
/// and <c>gh</c> cannot ask a question nobody is there to answer. Both steps
/// are named by <c>--dry-run</c> before either happens, which is the only way
/// to see what it would do without it having happened.
/// </para>
/// <para>
/// Nothing here rewrites anything. It pushes a branch that exists to a remote
/// that expects it, and opens a pull request against a base; if the branch is
/// already there the push is a no-op and gh says the pull request exists.
/// </para>
/// </remarks>
[Description("Open a pull request for what one node of a run produced.")]
[CommandMeta(CommandCategory.Start, Intent = "pr pull request github open branch node run", Mutates = true)]
public sealed class TeamPrCommand : AsyncCommand<TeamPrCommand.Settings>
{
    private readonly IRunJournal _journal;
    private readonly IGitManager _git;
    private readonly IExecutableResolver _resolver;
    private readonly IProcessLauncher _processes;
    private readonly IAnsiConsole _console;

    public TeamPrCommand(
        IRunJournal journal,
        IGitManager git,
        IExecutableResolver resolver,
        IProcessLauncher processes,
        IAnsiConsole console)
    {
        _journal = journal;
        _git = git;
        _resolver = resolver;
        _processes = processes;
        _console = console;
    }

    public sealed class Settings : RunSettings
    {
        [CommandOption("--node <NODE>")]
        [Description("Whose work, as the run names it. The only one with a branch when there is one.")]
        public string? Node { get; init; }

        [CommandOption("--base <BRANCH>")]
        [Description("What to open it against. The repository's own default when left out.")]
        public string? Base { get; init; }

        [CommandOption("--title <TITLE>")]
        [Description("The pull request's title. The run's goal when left out.")]
        public string? Title { get; init; }

        [CommandOption("--remote <NAME>")]
        [Description("Where to push. origin when left out.")]
        public string Remote { get; init; } = "origin";

        [CommandOption("--draft")]
        [Description("Open it as a draft.")]
        public bool Draft { get; init; }
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        var (id, error) = RunControlling.Resolve(_journal, settings.Run);

        if (id is null)
        {
            return output.Fail(error!, ExitCode.ProjectNotFound);
        }

        var read = _journal.Summarise(id);

        if (read.Failed)
        {
            return output.Fail(read.Error!, ExitCode.ProjectNotFound);
        }

        var run = read.Value!;

        if (run.Path is not { Length: > 0 } repository)
        {
            return output.Fail(
                $"{id} did not write down which repository it worked on, so there is nothing to push.",
                ExitCode.RepositoryUnavailable);
        }

        var node = Pick(run, settings.Node);

        if (node is null)
        {
            var branched = run.Nodes.Where(one => one.Branch is { Length: > 0 }).ToList();

            return output.Fail(
                branched.Count == 0
                    ? $"No node of {id} worked on a branch of its own, so there is nothing to open."
                    : $"Say which with --node: {string.Join(", ", branched.Select(one => one.Node))}.",
                ExitCode.InvalidArguments);
        }

        var branch = node.Branch!;
        var title = settings.Title is { Length: > 0 } given ? given : Title(run, node);
        var body = Body(run, node);

        if (settings.DryRun)
        {
            output.WriteLine(
                $"[dim]Dry run: nothing was pushed and nothing was opened.[/] It would push "
                + $"[bold]{Markup.Escape(branch)}[/] to {Shown.Safely(settings.Remote)}, then open a "
                + (settings.Draft ? "draft " : string.Empty)
                + "pull request titled:");

            output.WriteLine($"  {Markup.Escape(title)}");

            return CommandOutput.Success();
        }

        // Found and signed in, in that order. An installed but unauthenticated
        // gh offers a route that fails after the push has already happened.
        if (_resolver.Resolve("gh") is not { } gh)
        {
            return output.Fail(
                "The GitHub CLI is not on PATH. Install gh, or open the pull request yourself from "
                + $"branch '{branch}'.",
                ExitCode.AgentUnavailable);
        }

        var signedIn = await _processes.RunAsync(
            new ProcessRequest(gh, ["auth", "status"], repository),
            TimeSpan.FromSeconds(20),
            cancellationToken).ConfigureAwait(false);

        if (signedIn.Failed || signedIn.Value?.Succeeded != true)
        {
            return output.Fail(
                "The GitHub CLI is not signed in. Run: gh auth login", ExitCode.AuthenticationRequired);
        }

        var pushed = await _git.PushWithUpstreamAsync(
            repository, settings.Remote, branch, cancellationToken).ConfigureAwait(false);

        if (pushed.Failed)
        {
            return output.Fail($"{branch} could not be pushed: {pushed.Error}", pushed.ExitCode);
        }

        // A remote can be given as a URL rather than a name, and a URL can
        // carry a token. Escaping keeps a bracket from being read as markup and
        // says nothing at all about what the text contains.
        output.WriteLine($"[green]+[/] Pushed {Markup.Escape(branch)} to {Shown.Safely(settings.Remote)}.");

        var arguments = new List<string>
        {
            "pr", "create", "--head", branch, "--title", title, "--body", body,
        };

        if (settings.Base is { Length: > 0 } onto)
        {
            arguments.Add("--base");
            arguments.Add(onto);
        }

        if (settings.Draft)
        {
            arguments.Add("--draft");
        }

        var opened = await _processes.RunAsync(
            new ProcessRequest(gh, arguments, repository),
            TimeSpan.FromMinutes(2),
            cancellationToken).ConfigureAwait(false);

        if (opened.Failed || opened.Value?.Succeeded != true)
        {
            return output.Fail(
                "The pull request could not be opened: "
                + (opened.Value?.StandardError.Trim() is { Length: > 0 } said ? said : opened.Error),
                ExitCode.GeneralFailure);
        }

        // gh prints the URL and nothing else useful, and the URL is the one
        // thing somebody wants next.
        output.WriteLine($"[green]+[/] {Shown.Safely(opened.Value.StandardOutput.Trim())}");

        return CommandOutput.Success();
    }

    /// <summary>Which node's work this is about.</summary>
    /// <remarks>
    /// Named, or the only one with a branch. Guessing between two would be
    /// guessing which piece of work somebody meant to publish.
    /// </remarks>
    private static RunNode? Pick(RunSummary run, string? named)
    {
        var branched = run.Nodes.Where(one => one.Branch is { Length: > 0 }).ToList();

        if (named is { Length: > 0 })
        {
            return branched.FirstOrDefault(one =>
                string.Equals(one.Node, named, StringComparison.OrdinalIgnoreCase));
        }

        return branched.Count == 1 ? branched[0] : null;
    }

    private static string Title(RunSummary run, RunNode node) =>
        run.Goal is { Length: > 0 } goal
            ? (goal.Length > 70 ? goal[..70] : goal)
            : $"{node.Node} of {run.RunId}";

    /// <summary>
    /// What the pull request says about where it came from.
    /// </summary>
    /// <remarks>
    /// Whoever reviews this did not watch the run, so the body carries what
    /// they would otherwise have to ask for: what the run was for, which node
    /// did it, what the checker made of its report, and what it cost.
    /// </remarks>
    private static string Body(RunSummary run, RunNode node)
    {
        var turn = run.Turns.LastOrDefault(one =>
            string.Equals(one.Node, node.Node, StringComparison.Ordinal));

        var lines = new List<string>
        {
            run.Goal,
            string.Empty,
            $"From {node.Node} ({node.Role}) of team run {run.RunId}"
                + (run.Team is { Length: > 0 } team ? $", team {team}" : string.Empty)
                + ".",
        };

        if (turn is not null)
        {
            lines.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"It reported {turn.Status ?? "nothing"} and the run {turn.Outcome ?? "did not say"}, "
                + $"over {turn.Exchanges} exchange(s) costing ${node.CostUsd:0.00}."));
        }

        if (node.Said is { Length: > 0 } said)
        {
            lines.Add(string.Empty);
            lines.Add("In its own words:");
            lines.Add(string.Empty);
            lines.Add("> " + said.ReplaceLineEndings("\n> "));
        }

        return string.Join("\n", lines);
    }
}
