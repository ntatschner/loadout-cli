using Loadout.Cli.Infrastructure;
using Loadout.Core.Ideas;
using Loadout.Models.Ideas;
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
    public static string Stage(IdeaStage stage) => stage switch
    {
        IdeaStage.Captured => "captured",
        IdeaStage.Answering => "answering",
        IdeaStage.Ready => "ready",
        IdeaStage.Proposed => "proposed",
        _ => "accepted",
    };

    /// <summary>Where it stands, in the words a list shows.</summary>
    public static string Marked(IdeaStage stage, int unanswered) => stage switch
    {
        IdeaStage.Captured => "[dim]not refined yet[/]",
        IdeaStage.Answering => $"[yellow]{unanswered} to answer[/]",
        IdeaStage.Ready => "[blue]ready to refine[/]",
        IdeaStage.Proposed => "[green]plan proposed[/]",
        _ => "[dim]accepted[/]",
    };

    public static object Of(IdeaPlace place, IdeaRecord record)
    {
        var stage = IdeaWork.StageOf(record);

        return new
        {
            id = place.Id,
            project = place.Project,
            stage = Stage(stage),
            ask = record.Ask,
            captured = record.CapturedUtc,
            error = record.LastError.Length > 0 ? record.LastError : null,
            questions = record.Rounds.SelectMany(round => round.Questions).Select(q => new
            {
                id = q.Id,
                question = q.Question,
                why = q.Why,
                options = q.Options,
                recommendation = q.Recommendation,
                answer = q.Answer.Length > 0 ? q.Answer : null,
            }),
            request = record.Request.Length > 0 ? record.Request : null,
            plan = record.Plan is not { } plan ? null : new
            {
                revision = plan.Revision,
                proposed = plan.ProposedUtc,
                title = plan.Title,
                understanding = plan.Understanding,
                layers = plan.Layers.Select(layer => new
                {
                    id = layer.Id,
                    name = layer.Name,
                    purpose = layer.Purpose,
                    chosen = layer.Chosen,
                    verdict = layer.Verdict.ToString().ToLowerInvariant(),
                    request = layer.Request.Length > 0 ? layer.Request : null,
                    options = layer.Options.Select(option => new
                    {
                        id = option.Id,
                        title = option.Title,
                        detail = option.Detail,
                        pros = option.Pros,
                        cons = option.Cons,
                        recommended = option.Recommended,
                    }),
                }),
                additions = plan.Additions.Select(addition => new
                {
                    id = addition.Id,
                    title = addition.Title,
                    why = addition.Why,
                    verdict = addition.Verdict.ToString().ToLowerInvariant(),
                    request = addition.Request.Length > 0 ? addition.Request : null,
                }),
                placement = new
                {
                    slug = plan.Project.Slug.Length > 0 ? plan.Project.Slug : null,
                    isNew = plan.Project.IsNew,
                    name = plan.Project.Name.Length > 0 ? plan.Project.Name : null,
                    reason = plan.Project.Reason,
                },
            },
            accepted = record.Accepted is not { } accepted ? null : new
            {
                project = accepted.Project,
                plan = accepted.PlanPath,
                by = accepted.AcceptedBy,
                when = accepted.AcceptedUtc,
            },
        };
    }

    public static void Write(CommandOutput output, IdeaPlace place, IdeaRecord record)
    {
        var stage = IdeaWork.StageOf(record);

        output.WriteLine(
            $"[bold]{Markup.Escape(place.Id)}[/] on {Markup.Escape(place.Where)}  {Marked(stage, IdeaWork.Unanswered(record).Count)}");
        output.WriteLine($"[dim]{Markup.Escape(record.Ask)}[/]");

        if (record.LastError.Length > 0)
        {
            output.WriteLine($"[yellow]The last round went wrong:[/] {Markup.Escape(record.LastError)}");
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

                    if (chosen && option.Detail.Length > 0)
                    {
                        output.WriteLine($"            [dim]{Markup.Escape(option.Detail)}[/]");
                    }
                }
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
