using Loadout.Cli.Infrastructure;
using Loadout.Core.Ideas;
using Loadout.Models.Ideas;
using Loadout.Tui;
using Spectre.Console;

namespace Loadout.Cli.Commands;

/// <summary>How an idea is shown: as JSON for a screen or a script, and as text for a person.</summary>
/// <remarks>
/// One JSON shape for every idea command, so the TUI and the dashboard read
/// the same thing whichever command they last ran, and a field added here
/// reaches both.
/// </remarks>
internal static class IdeaView
{
    public static string Stage(IdeaStage stage) => IdeaJson.Stage(stage);

    /// <summary>Where it stands, in the words a list shows.</summary>
    public static string Marked(IdeaStage stage, int unanswered) => stage switch
    {
        IdeaStage.Captured => "[dim]not refined yet[/]",
        IdeaStage.Answering => $"[yellow]{unanswered} to answer[/]",
        IdeaStage.Ready => "[blue]ready to refine[/]",
        IdeaStage.Proposed => "[green]plan proposed[/]",
        _ => "[dim]accepted[/]",
    };

    /// <summary>
    /// Where it stands in a list, a running round or a failed one first: a
    /// failed round left showing its stage reads as one still in progress,
    /// and a failure left showing while a new round runs reads as stuck.
    /// </summary>
    public static string Marked(IdeaSummary idea)
    {
        ArgumentNullException.ThrowIfNull(idea);

        return idea.Running ? "[blue]round running[/]"
            : idea.Failure is not null ? "[red]last round failed[/]"
            : Marked(idea.Stage, idea.Unanswered);
    }

    /// <summary>An agent and model as a person reads them.</summary>
    public static string Asked(string agent, string model) =>
        model.Length > 0 ? $"{agent}, model {model}" : $"{agent} with its default model";

    /// <summary>A failed round, as JSON, or null when there is none.</summary>
    public static object? Failure(IdeaAttempt? failure) => IdeaJson.Failure(failure);

    /// <summary>
    /// Says which agent and model a round asks and why, and what went wrong
    /// last time when that was something different.
    /// </summary>
    /// <param name="output">Where to say it.</param>
    /// <param name="choice">What the rule chose.</param>
    /// <param name="fallback">The machine's default agent, for when nothing named one.</param>
    public static void Choice(CommandOutput output, IdeaChoice choice, string fallback)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(choice);

        var asking = Asked(choice.Agent ?? fallback, choice.Model ?? string.Empty);

        output.WriteLine(choice.FromLastRound
            ? $"[dim]Asking {Markup.Escape(asking)}, as the last round did.[/]"
            : $"[dim]Asking {Markup.Escape(asking)}.[/]");

        if (choice.Failure is not { } failed)
        {
            return;
        }

        var tried = failed.Agent.Length > 0 ? $" with {Asked(failed.Agent, failed.Model)}" : string.Empty;

        output.WriteLine(choice.FailedDifferently
            ? $"[yellow]The latest attempt{Markup.Escape(tried)} went wrong, so this round does not repeat it:[/] "
              + Shown.Safely(failed.Error)
            : $"[yellow]The latest attempt{Markup.Escape(tried)} went wrong:[/] {Shown.Safely(failed.Error)}");
    }

    public static object Of(IdeaPlace place, IdeaRecord record) => IdeaJson.Of(place, record);

    /// <param name="output">Where it is written.</param>
    /// <param name="place">Which idea, on which list.</param>
    /// <param name="record">The idea.</param>
    /// <param name="full">
    /// Every option's detail, pros and cons, rather than the chosen option's
    /// detail only: what Studio shows, at the length a terminal can make
    /// hard to read.
    /// </param>
    public static void Write(CommandOutput output, IdeaPlace place, IdeaRecord record, bool full = false)
    {
        var stage = IdeaWork.StageOf(record);

        output.WriteLine(
            $"[bold]{Markup.Escape(place.Id)}[/] on {Markup.Escape(place.Where)}  {Marked(stage, IdeaWork.Unanswered(record).Count)}");
        output.WriteLine($"[dim]{Markup.Escape(record.Ask)}[/]");

        if (IdeaWork.FailureOf(record) is { } failed)
        {
            var tried = failed.Agent.Length > 0 ? $" ({Asked(failed.Agent, failed.Model)})" : string.Empty;

            output.WriteLine(
                $"[yellow]The last round went wrong{Markup.Escape(tried)}:[/] {Shown.Safely(failed.Error)}");
            output.WriteLine(
                $"[dim]Refine again, or clear this with: loadout idea dismiss {Markup.Escape(place.Id)}[/]");
        }

        var questions = record.Rounds.SelectMany(round => round.Questions).ToList();

        if (questions.Count > 0)
        {
            output.WriteBlankLine();
            output.WriteLine("[bold]Questions[/]");

            foreach (var question in questions)
            {
                output.WriteLine($"  [bold]{question.Id}[/] {Markup.Escape(question.Question)}");

                if (question.Answer.Length > 0)
                {
                    output.WriteLine($"     [green]{Markup.Escape(question.Answer)}[/]");
                    continue;
                }

                if (question.Why.Length > 0)
                {
                    output.WriteLine($"     [dim]Why: {Markup.Escape(question.Why)}[/]");
                }

                if (question.Options.Count > 0)
                {
                    output.WriteLine($"     [dim]Likely: {Markup.Escape(string.Join(" / ", question.Options))}[/]");
                }

                if (question.Recommendation.Length > 0)
                {
                    output.WriteLine($"     [dim]Recommended: {Markup.Escape(question.Recommendation)}[/]");
                }
            }
        }

        if (record.Plan is { } plan)
        {
            output.WriteBlankLine();
            output.WriteLine($"[bold]{Markup.Escape(plan.Title)}[/]  [dim]revision {plan.Revision}[/]");
            output.WriteLine(Markup.Escape(plan.Understanding));

            foreach (var layer in plan.Layers)
            {
                output.WriteBlankLine();
                output.WriteLine(
                    $"  [bold]{layer.Id}[/] {Markup.Escape(layer.Name)}  {Verdict(layer.Verdict, layer.Request)}");

                if (layer.Purpose.Length > 0)
                {
                    output.WriteLine($"     [dim]{Markup.Escape(layer.Purpose)}[/]");
                }

                foreach (var option in layer.Options)
                {
                    var chosen = option.Id == layer.Chosen;

                    output.WriteLine(
                        $"     {(chosen ? "[green]>[/]" : " ")} {option.Id,-4} {Markup.Escape(option.Title)}"
                        + (option.Recommended ? " [dim](recommended)[/]" : string.Empty));

                    if ((chosen || full) && option.Detail.Length > 0)
                    {
                        output.WriteLine($"            [dim]{Markup.Escape(option.Detail)}[/]");
                    }

                    if (full)
                    {
                        foreach (var pro in option.Pros)
                        {
                            output.WriteLine($"            [green]+[/] {Markup.Escape(pro)}");
                        }

                        foreach (var con in option.Cons)
                        {
                            output.WriteLine($"            [red]-[/] {Markup.Escape(con)}");
                        }
                    }
                }
            }

            // Said only when there is something --full would add, so a plan
            // with nothing more to it does not point at an empty flag.
            if (!full && plan.Layers.Any(layer => layer.Options.Any(option =>
                    option.Pros.Count > 0 || option.Cons.Count > 0 || (option.Detail.Length > 0 && option.Id != layer.Chosen))))
            {
                output.WriteBlankLine();
                output.WriteLine(
                    $"[dim]Every option's detail, pros and cons: loadout idea show {Markup.Escape(place.Id)} --full[/]");
            }

            if (plan.Additions.Count > 0)
            {
                output.WriteBlankLine();
                output.WriteLine("  [bold]Suggested additions[/] [dim]in the plan only once kept[/]");

                foreach (var addition in plan.Additions)
                {
                    output.WriteLine(
                        $"  [bold]{addition.Id}[/] {Markup.Escape(addition.Title)}  {Verdict(addition.Verdict, addition.Request)}");

                    if (addition.Why.Length > 0)
                    {
                        output.WriteLine($"     [dim]{Markup.Escape(addition.Why)}[/]");
                    }
                }
            }

            output.WriteBlankLine();
            output.WriteLine(Placement(plan.Project));

            if (record.Request.Length > 0)
            {
                output.WriteLine($"[blue]Asked of the whole plan:[/] {Markup.Escape(record.Request)}");
            }
        }

        if (record.Accepted is { } accepted)
        {
            output.WriteBlankLine();
            output.WriteLine(
                $"Accepted onto [bold]{Markup.Escape(accepted.Project)}[/] by {Markup.Escape(accepted.AcceptedBy)}, "
                + $"{accepted.AcceptedUtc:yyyy-MM-dd}. The plan is at {Markup.Escape(accepted.PlanPath)}.");
        }

        WriteNext(output, place, record);
    }

    /// <summary>What can be done next, as the commands that do it.</summary>
    public static void WriteNext(CommandOutput output, IdeaPlace place, IdeaRecord record)
    {
        var id = Markup.Escape(place.Id);
        var next = IdeaWork.StageOf(record) switch
        {
            IdeaStage.Captured => [$"loadout idea refine {id}"],
            IdeaStage.Answering =>
            [
                $"loadout idea answer {id} {IdeaWork.Unanswered(record)[0].Id} \"...\"",
            ],
            IdeaStage.Ready => [$"loadout idea refine {id}"],
            IdeaStage.Proposed =>
            [
                $"loadout idea choose {id} <layer> <option>",
                $"loadout idea keep|drop {id} <pieces>",
                $"loadout idea improve {id} <piece|plan> \"...\"",
                $"loadout idea accept {id}",
            ],
            _ => new List<string>(),
        };

        if (next.Count == 0)
        {
            return;
        }

        output.WriteBlankLine();
        output.WriteLine("[bold]Next[/]");

        foreach (var line in next)
        {
            output.WriteLine($"  [dim]{line}[/]");
        }
    }

    private static string Verdict(IdeaVerdict verdict, string request) => verdict switch
    {
        IdeaVerdict.Keep => "[green]kept[/]",
        IdeaVerdict.Drop => "[red]dropped[/]",
        IdeaVerdict.Improve => $"[blue]to improve:[/] {Markup.Escape(request)}",
        _ => string.Empty,
    };

    private static string Placement(IdeaPlacement placement)
    {
        var reason = placement.Reason.Length > 0 ? $" [dim]{Markup.Escape(placement.Reason)}[/]" : string.Empty;

        if (placement.IsNew)
        {
            return "The agent thinks this is a project of its own"
                + (placement.Name.Length > 0 ? $", [bold]{Markup.Escape(placement.Name)}[/]" : string.Empty)
                + "." + reason;
        }

        return placement.Slug.Length > 0
            ? $"The agent places it on [bold]{Markup.Escape(placement.Slug)}[/].{reason}"
            : $"The agent could not say which project it belongs to.{reason}";
    }
}
