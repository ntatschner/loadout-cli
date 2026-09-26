using System.Globalization;
using Loadout.Core.Ideas;
using Loadout.Models.Ideas;

namespace Loadout.Tui.Terminal;

/// <summary>A command the ideas screen hands back, to be run once it has closed.</summary>
/// <param name="Command">The command, as it would be typed.</param>
/// <param name="Arguments">
/// Its arguments as a list, never joined into a line: answers and requests are
/// prose, and the catalogue splits a line on spaces.
/// </param>
/// <param name="Pause">
/// Whether what it prints is worth stopping to read before the screen comes
/// back: a round's questions or plan, or where an accepted idea went. Marking a
/// piece prints a line that says it worked, and stopping for that every time
/// would make working through a plan a chore.
/// </param>
/// <param name="Select">The idea to put the cursor on when the screen comes back, or null.</param>
internal sealed record IdeaStep(string Command, IReadOnlyList<string> Arguments, bool Pause, string? Select);

/// <summary>Where an accepted idea should go, as the person chose it.</summary>
/// <param name="Project">A registered project, or null.</param>
/// <param name="NewProject">A name for a project to make, or null.</param>
internal sealed record IdeaTarget(string? Project, string? NewProject);

/// <summary>
/// The command lines the ideas screen builds, one per thing it offers.
/// </summary>
/// <remarks>
/// <para>
/// Prose goes joined to its option, <c>--answer=...</c>. The parser refuses a
/// value that starts with a dash wherever else it stands, and an answer or a
/// request is prose somebody typed.
/// </para>
/// Every one names the list the idea is on outright, <c>--project</c> or
/// <c>--global</c>, rather than leaving the command to look: two lists can
/// hold ideas of the same name, and the one the cursor was on is the one meant.
/// </remarks>
internal static class IdeaSteps
{
    internal static IdeaStep Add(string text, string? project) => new(
        LauncherCommands.IdeaAdd,
        [$"--text={text}", .. List(project)],
        Pause: false,
        Select: null);

    internal static IdeaStep Refine(IdeaPlace place) => new(
        LauncherCommands.IdeaRefine, [place.Id, .. List(place.Project)], Pause: true, place.Id);

    internal static IdeaStep Answer(IdeaPlace place, string question, string answer) => new(
        LauncherCommands.IdeaAnswer, [place.Id, question, $"--answer={answer}", .. List(place.Project)], Pause: false, place.Id);

    internal static IdeaStep Choose(IdeaPlace place, string layer, string option) => new(
        LauncherCommands.IdeaChoose, [place.Id, layer, option, .. List(place.Project)], Pause: false, place.Id);

    internal static IdeaStep Judge(IdeaPlace place, IdeaVerdict verdict, IReadOnlyList<string> pieces) => new(
        verdict == IdeaVerdict.Keep ? LauncherCommands.IdeaKeep : LauncherCommands.IdeaDrop,
        [place.Id, .. pieces, .. List(place.Project)],
        Pause: false,
        place.Id);

    internal static IdeaStep Improve(IdeaPlace place, string piece, string request) => new(
        LauncherCommands.IdeaImprove, [place.Id, piece, $"--request={request}", .. List(place.Project)], Pause: false, place.Id);

    internal static IdeaStep Accept(IdeaPlace place, IdeaTarget target) => new(
        LauncherCommands.IdeaAccept,
        [
            place.Id,
            .. List(place.Project),
            .. target.NewProject is { Length: > 0 } made ? (string[])[$"--new-project={made}"] : ["--to", target.Project!],
        ],
        Pause: true,
        Select: null);

    internal static IdeaStep Remove(IdeaPlace place) => new(
        LauncherCommands.IdeaRemove, [place.Id, .. List(place.Project)], Pause: false, Select: null);

    private static string[] List(string? project) =>
        project is { Length: > 0 } ? ["--project", project] : ["--global"];

    /// <summary>
    /// What the detail pane shows for an idea, one line each, wrapped to the
    /// width given.
    /// </summary>
    /// <remarks>
    /// Plain text with the ids in front, because the ids are what every key
    /// asks for: a layer is picked as L2, an option as L2b. Words and not only
    /// marks for what was decided, for the reason the teams screen gives: a
    /// state somebody has to see the colour of is one some people cannot read.
    /// </remarks>
    internal static IReadOnlyList<string> Detail(IdeaPlace place, IdeaRecord? record, int width)
    {
        var lines = new List<string>();

        void Say(string text, int indent = 0)
        {
            foreach (var line in Wrap(text, Math.Max(20, width - indent)))
            {
                lines.Add(new string(' ', indent) + line);
            }
        }

        if (record is null)
        {
            Say($"{place.Id} has no working record, so it cannot be refined from here.");

            return lines;
        }

        var stage = IdeaWork.StageOf(record);

        Say($"{place.Id} on {place.Where}: {Stage(stage, IdeaWork.Unanswered(record).Count)}");
        Say(record.Ask, 2);

        if (record.LastError.Length > 0)
        {
            lines.Add(string.Empty);
            Say($"The last round went wrong: {record.LastError}");
        }

        var questions = record.Rounds.SelectMany(round => round.Questions).ToList();

        if (questions.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Questions");

            foreach (var question in questions)
            {
                Say($"{question.Id}  {question.Question}", 2);

                if (question.Answer.Length > 0)
                {
                    Say($"Answered: {question.Answer}", 6);
                }
                else
                {
                    if (question.Recommendation.Length > 0)
                    {
                        Say($"Recommended: {question.Recommendation}", 6);
                    }

                    Say("Not answered yet.", 6);
                }
            }
        }

        if (record.Plan is { } plan)
        {
            lines.Add(string.Empty);
            Say($"{plan.Title}  (revision {plan.Revision})");
            Say(plan.Understanding, 2);

            foreach (var layer in plan.Layers)
            {
                lines.Add(string.Empty);
                Say($"{layer.Id}  {layer.Name}{Verdict(layer.Verdict, layer.Request)}", 2);

                foreach (var option in layer.Options)
                {
                    var chosen = option.Id == layer.Chosen;

                    Say(
                        $"{(chosen ? "chosen " : "       ")}{option.Id,-5} {option.Title}"
                        + (option.Recommended ? " (recommended)" : string.Empty),
                        4);
                }
            }

            if (plan.Additions.Count > 0)
            {
                lines.Add(string.Empty);
                lines.Add("  Suggested additions, in the plan only once kept");

                foreach (var addition in plan.Additions)
                {
                    Say($"{addition.Id}  {addition.Title}{Verdict(addition.Verdict, addition.Request)}", 2);
                    Say(addition.Why, 6);
                }
            }

            lines.Add(string.Empty);
            Say(plan.Project.IsNew
                ? $"The agent thinks this is a project of its own{(plan.Project.Name.Length > 0 ? $", {plan.Project.Name}" : string.Empty)}."
                : plan.Project.Slug.Length > 0
                    ? $"The agent places it on {plan.Project.Slug}."
                    : "The agent could not say which project it belongs to.");

            if (record.Request.Length > 0)
            {
                Say($"Asked of the whole plan: {record.Request}");
            }
        }

        return lines;
    }

    /// <summary>Where an idea stands, in the words the list shows.</summary>
    internal static string Stage(IdeaStage stage, int unanswered) => stage switch
    {
        IdeaStage.Captured => "not refined yet",
        IdeaStage.Answering => string.Create(CultureInfo.InvariantCulture, $"{unanswered} to answer"),
        IdeaStage.Ready => "ready to refine",
        IdeaStage.Proposed => "plan proposed",
        _ => "accepted",
    };

    private static string Verdict(IdeaVerdict verdict, string request) => verdict switch
    {
        IdeaVerdict.Keep => "  [kept]",
        IdeaVerdict.Drop => "  [dropped]",
        IdeaVerdict.Improve => $"  [to improve: {request}]",
        _ => string.Empty,
    };

    /// <summary>Breaks text at spaces to fit a width, keeping any line break it already has.</summary>
    internal static IEnumerable<string> Wrap(string text, int width)
    {
        foreach (var paragraph in text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'))
        {
            var line = string.Empty;

            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Length > 0 && line.Length + 1 + word.Length > width)
                {
                    yield return line;
                    line = string.Empty;
                }

                line = line.Length == 0 ? word : $"{line} {word}";
            }

            yield return line;
        }
    }
}
