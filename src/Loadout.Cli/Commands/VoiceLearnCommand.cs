using System.ComponentModel;
using System.Globalization;
using Loadout.Agents.Voice;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Git;
using Loadout.Core.Instructions;
using Loadout.Core.Projects;
using Loadout.Core.Voice;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Models.Voice;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>Options for a learn run.</summary>
public sealed class VoiceLearnSettings : VoiceSettings
{
    [CommandOption("--layer <LAYER>")]
    [Description("Which profile to learn into: personal (the default), named or codebase.")]
    [DefaultValue("personal")]
    public string Layer { get; init; } = "personal";

    [CommandOption("--style <NAME>")]
    [Description("The named style to learn into, with --layer named.")]
    public string? Style { get; init; }

    [CommandOption("--corpus <DIRECTORY>")]
    [Description("A directory of somebody's writing to learn a named style from, with --layer named.")]
    public string? Corpus { get; init; }

    [CommandOption("--model <MODEL>")]
    [Description("The model to ask for, as the agent spells it.")]
    public string? Model { get; init; }

    [CommandOption("--pull-requests")]
    [Description("Also read merged pull requests through gh, read-only, to score rules against the descriptions you rewrote when merging.")]
    public bool PullRequests { get; init; }

    [CommandOption("--transcripts")]
    [Description("Also read what you typed to Claude Code, for a guide about word choice or spelling that lists transcripts. Your personal profile only.")]
    public bool Transcripts { get; init; }
}

/// <summary>
/// Learns rules from the person's own writing and asks them about each one.
/// </summary>
/// <remarks>
/// <para>
/// Interactive by nature: every rule is the person's decision, and the cap is
/// theirs to set before anything is spent. Without a terminal it refuses
/// rather than guessing, and <c>--dry-run</c> shows the plan and the estimate
/// without asking or starting anything.
/// </para>
/// <para>
/// Rounds read and never write: the agent is given the writing and no tools,
/// in a directory of its own. Nothing is pushed or fetched; marked files and
/// squash merges are read from the repository as it is. Pull requests are
/// read through gh only with <c>--pull-requests</c>, or when one is marked.
/// </para>
/// </remarks>
[Description("Learn how you write from your own writing, then accept, edit or reject each rule.")]
[CommandMeta(CommandCategory.AgentConfiguration,
    Intent = "learn my writing style voice prose rules from commits docs interview",
    Mutates = true,
    Example = "voice learn --dry-run")]
public sealed class VoiceLearnCommand : VoiceCommandBase<VoiceLearnSettings>
{
    private readonly IVoiceLearner _learner;
    private readonly ReadingProfile _reading;

    public VoiceLearnCommand(
        IVoiceLearner learner,
        ReadingProfile reading,
        VoiceStore store,
        IInstructionService instructions,
        IWorkspaceManager workspace,
        IProjectService projects,
        IGitManager git,
        IAnsiConsole console)
        : base(store, instructions, workspace, projects, git, console)
    {
        _learner = learner;
        _reading = reading;
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        VoiceLearnSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(Console, settings);

        if (!settings.DryRun && !settings.AllowsPrompting)
        {
            return output.Fail(
                "Learning asks you about every rule, so it needs a terminal. Use --dry-run to see the plan without one.",
                ExitCode.InvalidArguments);
        }

        var resolved = await ResolveAsync(settings, cancellationToken).ConfigureAwait(false);

        if (resolved.Failed)
        {
            return output.Fail(resolved);
        }

        var (guide, project, _) = resolved.Value!;

        if (VoiceGuides.IsSet(guide))
        {
            return output.Fail(
                $"{guide.Reference} is set, not learned: there is nothing in your writing to learn it from. "
                + "Use 'loadout voice time set'.",
                ExitCode.InvalidArguments);
        }

        StyleLayer layer;
        string name;

        switch (settings.Layer.Trim().ToLowerInvariant())
        {
            case "personal":
                layer = StyleLayer.Personal;
                name = CodingStyles.Personal;
                break;

            case "codebase" when project is not null:
                layer = StyleLayer.Codebase;
                name = CodingStyles.Codebase;
                break;

            case "codebase":
                return output.Fail(
                    "A codebase profile belongs to a registered project, and this is not one. Pass --project.",
                    ExitCode.InvalidArguments);

            case "named" when settings.Style is { Length: > 0 } style
                && CodingStyles.LayerOf(style.Trim().ToLowerInvariant(), style.Trim().ToLowerInvariant()) == StyleLayer.Named:
                layer = StyleLayer.Named;
                name = style.Trim().ToLowerInvariant();
                break;

            case "named":
                return output.Fail("--layer named needs --style with the style's name.", ExitCode.InvalidArguments);

            default:
                return output.Fail(
                    $"'{settings.Layer}' is not a layer. Choose personal, named or codebase.", ExitCode.InvalidArguments);
        }

        if (settings.Corpus is not null && layer != StyleLayer.Named)
        {
            return output.Fail(
                "A corpus is somebody else's writing, so it can only teach a named style. Use --layer named.",
                ExitCode.InvalidArguments);
        }

        // Your prompts are yours, so they teach your own profile and nothing
        // else: not a named style learned from somebody's corpus, and not a
        // codebase, which is everybody who works on it.
        if (settings.Transcripts && layer != StyleLayer.Personal)
        {
            return output.Fail(
                "Transcripts are your own prompts, so they can only teach your personal profile. Use --layer personal.",
                ExitCode.InvalidArguments);
        }

        if (settings.Transcripts && !guide.Sources.Contains(VoiceGuideDrafting.Transcript, StringComparer.Ordinal))
        {
            return output.Fail(
                $"{guide.Reference} does not read transcripts. Only a guide about word choice or spelling can, "
                + "and it has to list them: draft one with 'loadout voice guide draft'.",
                ExitCode.InvalidArguments);
        }

        // A corpus-only named style reads no repository: somebody else's style
        // is not to be mixed with whatever this repository's history holds.
        var repository = settings.Corpus is null
            ? await RepositoryAsync(project, settings.Repo, cancellationToken).ConfigureAwait(false)
            : null;

        var prompts = new TerminalVoicePrompts(Console, _reading, output);

        var learned = await _learner.LearnAsync(
            new VoiceLearnRequest(
                guide,
                layer,
                name,
                project?.Entry.Slug,
                repository,
                settings.Corpus is null ? null : Path.GetFullPath(settings.Corpus),
                prompts,
                PullRequests: settings.PullRequests,
                AgentName: settings.Agent,
                Model: settings.Model,
                DryRun: settings.DryRun,
                Transcripts: settings.Transcripts),
            cancellationToken).ConfigureAwait(false);

        if (learned.Failed)
        {
            return output.Fail(learned);
        }

        var outcome = learned.Value!;

        // Rendered as soon as the profile is written, so what was accepted is
        // what the next session loads. A render that cannot happen, with no
        // workspace or past a hand edit, is said and does not undo the run.
        var rendered = settings.DryRun
            ? null
            : await Store.RenderAsync(layer, name, project?.Entry.Slug, ct: cancellationToken).ConfigureAwait(false);

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                dryRun = settings.DryRun,
                profile = outcome.ProfilePath,
                rendered = rendered switch
                {
                    null => null,
                    { Failed: true } => (object)new { error = Loadout.Tui.Shown.Plainly(rendered.Error) },
                    _ => new
                    {
                        id = rendered.Value!.Id,
                        path = rendered.Value.Path,
                        state = VoiceRenderReport.State(rendered.Value.State),
                        estimatedTokens = rendered.Value.EstimatedTokens,
                    },
                },
                evidence = outcome.Gathered.Evidence.Count - outcome.HeldOut,
                heldOut = outcome.HeldOut,
                editPairs = outcome.Gathered.EditPairs.Count,
                refused = outcome.Gathered.Refused,
                agentWritten = outcome.Gathered.AgentWritten,
                unconfirmed = outcome.Gathered.Unconfirmed,
                estimate = new { calls = outcome.Estimate.Calls, usd = outcome.Estimate.Usd, fromHistory = outcome.Estimate.FromHistory },
                run = VoiceRunView.Of(outcome.Run),
                dropped = outcome.Dropped,
                weakened = outcome.Weakened,
                failures = outcome.Failures,
            });

            return CommandOutput.Success();
        }

        Report(output, outcome, settings.DryRun);

        if (rendered is { Failed: true })
        {
            output.WriteLine($"[yellow]Not rendered:[/] {Loadout.Tui.Shown.Safely(rendered.Error!)}");
        }
        else if (rendered is { Value: { } done })
        {
            output.WriteLine(VoiceRenderReport.Line(done, dryRun: false, layer, name));
        }

        return CommandOutput.Success();
    }

    private static void Report(CommandOutput output, VoiceLearnOutcome outcome, bool dryRun)
    {
        var gathered = outcome.Gathered;

        if (dryRun)
        {
            output.WriteLine(
                $"[bold]Would read[/] {gathered.Evidence.Count - outcome.HeldOut} piece(s) of your writing in "
                + $"{outcome.Estimate.Calls} call(s)"
                + (VoiceScoring.Calls(outcome.HeldOut, gathered.EditPairs.Count) > 0 ? " (two of them for scoring)" : string.Empty)
                + $", estimated at {Usd(outcome.Estimate.Usd)}"
                + (outcome.Estimate.FromHistory ? " from earlier runs." : " (a first guess; there are no earlier runs to go on)."));

            foreach (var source in gathered.Evidence.GroupBy(e => e.Source).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                output.WriteLine($"  [dim]{source.Count()} {Markup.Escape(source.Key)}[/]");
            }

            output.WriteLine(
                $"[dim]{outcome.HeldOut} of them held back, never mined, to score the rules against"
                + (gathered.EditPairs.Count > 0 ? $", with {gathered.EditPairs.Count} description(s) you rewrote when merging." : ".")
                + "[/]");

            if (outcome.Run.Carried > 0)
            {
                output.WriteLine($"[dim]{outcome.Run.Carried} proposal(s) carried from an earlier run would be offered first.[/]");
            }
        }
        else
        {
            var run = outcome.Run;

            output.WriteBlankLine();
            output.WriteLine(
                $"[bold]{Outcome(run.Outcome)}.[/] Accepted {run.Accepted}, rejected {run.Rejected}, "
                + $"left {run.Carried} for next time. {run.Calls} call(s), {Usd(run.CostUsd)}.");
            output.WriteLine($"[dim]Profile: {Markup.Escape(outcome.ProfilePath)}[/]");
            output.WriteLine($"[dim]{Scored(run)}[/]");
        }

        if (gathered.Evidence.Count == 0)
        {
            output.WriteLine(
                "[yellow]None of your writing was found to learn from.[/] Only squash merges and what you have marked "
                + "count. Mark some with: loadout voice mark <file or commit range>");
        }

        if (gathered.AgentWritten > 0)
        {
            output.WriteLine(
                $"[dim]{gathered.AgentWritten} squash merge(s) were left out because they name an assistant as an author.[/]");
        }

        // A real run said this before the cap; a dry run never got that far.
        if (dryRun && gathered.OnlyUnconfirmed)
        {
            output.WriteLine($"[yellow]{Markup.Escape(VoiceGathering.UnconfirmedWarning)}[/]");
        }

        if (gathered.Refused > 0)
        {
            output.WriteLine(
                $"[dim]{gathered.Refused} piece(s) were left out because they could not be checked for credentials.[/]");
        }

        foreach (var id in outcome.Dropped)
        {
            output.WriteLine($"[dim]Dropped carried proposal {Markup.Escape(id)}: the writing it cited has changed.[/]");
        }

        foreach (var id in outcome.Weakened)
        {
            output.WriteLine(
                $"[yellow]{Markup.Escape(id)}[/] [dim]is now short of the evidence it was accepted on. "
                + "Re-confirm lapsed marks with: loadout voice mark --lapsed[/]");
        }

        foreach (var failure in outcome.Failures)
        {
            output.WriteLine($"[yellow]A round could not be used:[/] {Markup.Escape(failure)}");
        }
    }

    /// <summary>A run's score in a line, or why it has none.</summary>
    internal static string Scored(VoiceRun run)
    {
        if (run.Score is not { } score)
        {
            return run.Outcome == VoiceRunOutcome.Complete
                ? "No score."
                : "No score: only a complete run is scored.";
        }

        var scored = score.Rules.Count(r => r.Agreement is not null);

        return $"Scored {scored} of {score.Rules.Count} rule(s) against {score.Own} held-out piece(s) "
            + $"and {score.Edits} edit pair(s)"
            + (scored > 0
                ? $", {score.Rules.Where(r => r.Agreement is not null).Average(r => r.Agreement!.Value).ToString("P0", CultureInfo.InvariantCulture)} agreement on average."
                : ".");
    }

    internal static string Usd(decimal amount) =>
        "$" + amount.ToString("0.00", CultureInfo.InvariantCulture);

    internal static string Outcome(VoiceRunOutcome outcome) => outcome switch
    {
        VoiceRunOutcome.Complete => "Complete",
        VoiceRunOutcome.Partial => "Partial",
        _ => "Review only",
    };
}

/// <summary>A run as JSON shows it.</summary>
internal static class VoiceRunView
{
    public static object Of(VoiceRun run) => new
    {
        id = run.Id,
        guide = run.Guide,
        outcome = run.Outcome switch
        {
            VoiceRunOutcome.Complete => "complete",
            VoiceRunOutcome.Partial => "partial",
            _ => "review-only",
        },
        cap = run.Cap.ToString().ToLowerInvariant(),
        calls = run.Calls,
        costUsd = run.CostUsd,
        evidence = run.Evidence,
        proposed = run.Proposed,
        accepted = run.Accepted,
        rejected = run.Rejected,
        carried = run.Carried,
        score = run.Score,
        finished = run.Finished,
    };
}

/// <summary>Asks a learn run's questions in the terminal.</summary>
internal sealed class TerminalVoicePrompts : IVoiceLearnPrompts
{
    private readonly IAnsiConsole _console;
    private readonly ReadingProfile _reading;
    private readonly CommandOutput _output;

    public TerminalVoicePrompts(IAnsiConsole console, ReadingProfile reading, CommandOutput output)
    {
        _console = console;
        _reading = reading;
        _output = output;
    }

    public void Progress(string message) => _output.Meanwhile($"[dim]{Markup.Escape(message)}[/]");

    public VoiceCapAnswer Cap(VoiceCapQuestion question, VoiceEstimate estimate, VoiceCap proposed, VoiceCap? stored)
    {
        var why = question switch
        {
            VoiceCapQuestion.First => "This is the first run, so there is no spending cap yet.",
            VoiceCapQuestion.Larger => $"There is more to read than last time: the estimate is over the {VoiceLearnCommand.Usd(stored!.Usd)} cap.",
            _ => $"There is much less to read than when the {VoiceLearnCommand.Usd(stored!.Usd)} cap was set.",
        };

        _output.WriteLine(why);
        _output.WriteLine(
            $"[dim]Estimate: {estimate.Calls} call(s), about {VoiceLearnCommand.Usd(estimate.Usd)}"
            + (estimate.FromHistory ? ", from what earlier runs cost." : ", a first guess at what a call costs.") + "[/]");

        var accept = $"Cap it at {VoiceLearnCommand.Usd(proposed.Usd)} and {proposed.Calls} call(s), the estimate plus a quarter";
        var decline = question == VoiceCapQuestion.Lower
            ? "Keep the cap as it is"
            : "No, spend nothing; review anything carried from last time";
        const string typed = "Type my own figure";

        var choice = _reading.Ask(_console, "What cap should this run have?", [accept, decline, typed], c => c);

        if (choice == accept)
        {
            return new VoiceCapAnswer(VoiceCapChoice.Accept);
        }

        if (choice == decline)
        {
            return new VoiceCapAnswer(VoiceCapChoice.Decline);
        }

        var figure = _console.Prompt(
            new TextPrompt<decimal>("Cap in US dollars:")
                .Validate(v => v > 0 ? ValidationResult.Success() : ValidationResult.Error("A cap is more than nothing.")));

        if (figure < estimate.Usd)
        {
            _output.WriteLine(
                "[yellow]That is below the estimate, so the run will probably stop short and end partial.[/] "
                + "[dim]What it reads before then is kept for the next run.[/]");
        }

        return new VoiceCapAnswer(VoiceCapChoice.Typed, figure);
    }

    public bool PastCap(VoiceCap cap, int calls, decimal spent, int remaining) =>
        _reading.Confirm(
            _console,
            $"The cap is reached ({calls} call(s), {VoiceLearnCommand.Usd(spent)} of {VoiceLearnCommand.Usd(cap.Usd)}), "
            + $"with {remaining} round(s) left. Carry on past it?",
            byDefault: false);

    public VoiceReviewAnswer Review(VoiceReviewItem item)
    {
        var rule = item.Rule;

        _output.WriteBlankLine();
        _output.WriteLine(
            $"[bold]{item.Number} of {item.Of}[/]  [cyan]{Markup.Escape(rule.Id)}[/]  "
            + $"[dim]{(rule.Check is { } check ? check.Type : "judged")}, "
            + $"{item.Evidence.Citations} citation(s) from {item.Evidence.Sources} kind(s) of writing[/]");

        if (item.Replaces is { } old)
        {
            _output.WriteLine($"[dim]Changes: {Markup.Escape(old.Statement)}[/]");
        }

        _output.WriteLine(Markup.Escape(rule.Statement));

        foreach (var citation in rule.Citations.Take(3))
        {
            _output.WriteLine($"  [dim]{Markup.Escape(citation.Source)}, {Markup.Escape(Short(citation.Reference))}:[/] \"{Markup.Escape(citation.Excerpt)}\"");
        }

        var title = item.Asked
            ? "There is not quite enough of your writing to be sure. Is this how you write?"
            : "Keep this rule?";

        var choices = new[] { VoiceReviewChoice.Accept, VoiceReviewChoice.Edit, VoiceReviewChoice.Reject, VoiceReviewChoice.Skip };

        var choice = _reading.Ask(_console, title, choices, c => c switch
        {
            VoiceReviewChoice.Accept => item.Asked ? "Yes, keep it" : "Accept",
            VoiceReviewChoice.Edit => "Keep it, in my own words",
            VoiceReviewChoice.Reject => "No, reject it",
            _ => "Not now, ask me next time",
        });

        if (choice != VoiceReviewChoice.Edit)
        {
            return new VoiceReviewAnswer(choice);
        }

        var statement = _console.Prompt(new TextPrompt<string>("The rule, as you would put it:").DefaultValue(rule.Statement));

        return new VoiceReviewAnswer(VoiceReviewChoice.Edit, statement);
    }

    private static string Short(string reference) =>
        reference.Length == 40 && reference.All(Uri.IsHexDigit) ? reference[..12] : reference;
}

/// <summary>Options for the run history.</summary>
public sealed class VoiceRunsSettings : VoiceSettings
{
    [CommandOption("--layer <LAYER>")]
    [Description("Which profile's runs: personal (the default), named or codebase.")]
    [DefaultValue("personal")]
    public string Layer { get; init; } = "personal";

    [CommandOption("--style <NAME>")]
    [Description("The named style, with --layer named.")]
    public string? Style { get; init; }
}

/// <summary>Lists past learn runs: how each ended, what it spent and what it changed.</summary>
[Description("List past learn runs: how each ended, how its cap was set, and what it cost.")]
[CommandMeta(CommandCategory.AgentConfiguration, Intent = "voice learn history runs cost partial cap")]
public sealed class VoiceRunsCommand : VoiceCommandBase<VoiceRunsSettings>
{
    public VoiceRunsCommand(
        VoiceStore store,
        IInstructionService instructions,
        IWorkspaceManager workspace,
        IProjectService projects,
        IGitManager git,
        IAnsiConsole console)
        : base(store, instructions, workspace, projects, git, console)
    {
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        VoiceRunsSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(Console, settings);

        var resolved = await ResolveAsync(settings, cancellationToken).ConfigureAwait(false);

        if (resolved.Failed)
        {
            return output.Fail(resolved);
        }

        var (guide, project, _) = resolved.Value!;

        var (layer, name) = settings.Layer.Trim().ToLowerInvariant() switch
        {
            "named" when settings.Style is { Length: > 0 } style => (StyleLayer.Named, style.Trim().ToLowerInvariant()),
            "codebase" => (StyleLayer.Codebase, CodingStyles.Codebase),
            _ => (StyleLayer.Personal, CodingStyles.Personal),
        };

        var state = await VoiceStore
            .ReadStateAsync(Store.StatePath(layer, name, guide.Id, project?.Entry.Slug), cancellationToken)
            .ConfigureAwait(false);

        if (state.Failed)
        {
            return output.Fail(state);
        }

        var learn = state.Value!;

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                cap = learn.Cap,
                carried = learn.Carried.Count,
                runs = learn.Runs.Select(VoiceRunView.Of),
            });

            return CommandOutput.Success();
        }

        if (learn.Runs.Count == 0)
        {
            output.WriteLine("[dim]No learn runs yet. Start one with: loadout voice learn[/]");

            return CommandOutput.Success();
        }

        foreach (var run in learn.Runs)
        {
            output.WriteLine(
                $"[cyan]{Markup.Escape(run.Id)}[/]  {VoiceLearnCommand.Outcome(run.Outcome).ToLowerInvariant()}, "
                + $"cap {run.Cap.ToString().ToLowerInvariant()}, {run.Calls} call(s), {VoiceLearnCommand.Usd(run.CostUsd)}, "
                + $"accepted {run.Accepted} of {run.Proposed}, "
                + (run.Score is { } score
                    ? $"scored {score.Rules.Count(r => r.Agreement is not null)} of {score.Rules.Count} rule(s)"
                    : "[dim]no score[/]"));
        }

        if (learn.Cap is { } cap)
        {
            output.WriteBlankLine();
            output.WriteLine($"[dim]Cap: {cap.Calls} call(s) or {VoiceLearnCommand.Usd(cap.Usd)}, whichever comes first.[/]");
        }

        if (learn.Carried.Count > 0)
        {
            output.WriteLine($"[dim]{learn.Carried.Count} proposal(s) waiting for the next run.[/]");
        }

        return CommandOutput.Success();
    }
}
