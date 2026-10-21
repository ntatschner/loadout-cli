using System.Globalization;
using System.Text;
using Loadout.Models.Ideas;

namespace Loadout.Core.Ideas;

/// <summary>A registered project, as an agent is told about it when placing an idea.</summary>
public sealed record IdeaProjectHint(string Slug, string Name);

/// <summary>Where an accepted idea should go, and why.</summary>
/// <param name="Project">A registered project to put it on, or null.</param>
/// <param name="NewProject">A name for a project to make for it, or null.</param>
/// <param name="Reason">Why, or why it could not be settled.</param>
public sealed record IdeaDestination(string? Project, string? NewProject, string Reason)
{
    /// <summary>True when neither was settled and the person has to say.</summary>
    public bool Unsettled => Project is null && NewProject is null;
}

/// <summary>Which agent and model a round will ask, and why.</summary>
/// <param name="Agent">The agent, or null for the machine's default.</param>
/// <param name="Model">The model, or null for the agent's own default.</param>
/// <param name="FromLastRound">True when neither was asked for and the last round's were taken.</param>
/// <param name="Failure">The latest round that went wrong, when there is one, for saying so.</param>
public sealed record IdeaChoice(string? Agent, string? Model, bool FromLastRound, IdeaAttempt? Failure)
{
    /// <summary>
    /// True when the failure was asked with a different agent or model than
    /// this round will be, which is worth saying: the person may have meant to
    /// try that one again.
    /// </summary>
    public bool FailedDifferently =>
        Failure is { } failed
        && (!string.Equals(failed.Agent, Agent ?? string.Empty, StringComparison.Ordinal)
            || !string.Equals(failed.Model, Model ?? string.Empty, StringComparison.Ordinal));
}

/// <summary>
/// What an idea's record says about where it stands and what the next round
/// is for, and how an agent's answer is folded into it.
/// </summary>
/// <remarks>
/// Kept apart from the storage and from the launch so the whole conversation
/// can be tested without either: this is where the decisions are, and neither a
/// file nor an agent is needed to check them.
/// </remarks>
public static class IdeaWork
{
    /// <summary>The piece that names the whole plan rather than one part of it.</summary>
    public const string WholePlan = "plan";

    /// <summary>Where an idea stands.</summary>
    public static IdeaStage StageOf(IdeaRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (record.Accepted is not null)
        {
            return IdeaStage.Accepted;
        }

        if (Unanswered(record).Count > 0)
        {
            return IdeaStage.Answering;
        }

        if (record.Plan is null)
        {
            return record.Rounds.Count == 0 ? IdeaStage.Captured : IdeaStage.Ready;
        }

        return AnythingNewFor(record, record.Plan) ? IdeaStage.Ready : IdeaStage.Proposed;
    }

    /// <summary>
    /// Why a note cannot be added to this idea, or null when it can. One rule
    /// for the write and for a dry run, so the two never disagree.
    /// </summary>
    /// <remarks>
    /// Only acceptance refuses. An accepted idea is a task now, and changing
    /// what it is belongs to the task. Every other stage takes a note, and the
    /// next round reads it.
    /// </remarks>
    public static string? NoteRefusal(IdeaPlace place, IdeaRecord record)
    {
        ArgumentNullException.ThrowIfNull(place);
        ArgumentNullException.ThrowIfNull(record);

        return record.Accepted is { } accepted
            ? $"That idea has been accepted, onto {accepted.Project}, so its plan is settled. Nothing was added."
            : null;
    }

    /// <summary>
    /// Adds a note to the record, numbered after every note it has had, and
    /// hands it back.
    /// </summary>
    public static IdeaNote AddNote(IdeaRecord record, string text, string by, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(text);

        var last = record.Notes
            .Select(note => note.Id.Length > 1 && int.TryParse(note.Id.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();

        var note = new IdeaNote
        {
            Id = $"N{(last + 1).ToString(CultureInfo.InvariantCulture)}",
            Text = text.Trim(),
            By = by,
            AddedUtc = now,
        };

        record.Notes.Add(note);

        return note;
    }

    /// <summary>Notes no round has read yet.</summary>
    public static IReadOnlyList<IdeaNote> Unread(IdeaRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return [.. record.Notes.Where(note => note.TakenUtc is null)];
    }

    /// <summary>Questions still waiting on the person.</summary>
    public static IReadOnlyList<IdeaQuestion> Unanswered(IdeaRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return [.. record.Rounds.SelectMany(round => round.Questions).Where(q => q.Answer.Length == 0)];
    }

    /// <summary>
    /// The latest round that went wrong, or null. A record written before the
    /// agent and model were kept says only what went wrong, and is read as a
    /// failure with neither named.
    /// </summary>
    public static IdeaAttempt? FailureOf(IdeaRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return record.Failure
            ?? (record.LastError.Length > 0 ? new IdeaAttempt { Error = record.LastError } : null);
    }

    /// <summary>Records a round that went wrong, replacing any earlier one.</summary>
    public static void Failed(IdeaRecord record, IdeaAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(attempt);

        record.Failure = attempt;
        record.LastError = string.Empty;
    }

    /// <summary>Forgets the latest failure. Nothing else about the idea changes.</summary>
    public static void ClearFailure(IdeaRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        record.Failure = null;
        record.LastError = string.Empty;
    }

    /// <summary>
    /// The agent and model the next round asks, from what was asked for and
    /// what the last round that came back used.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The last round that came back, not the last attempt. A failed round is
    /// often a mistyped model, and taking it as the default would repeat the
    /// typo on every round until somebody noticed. The failure is handed back
    /// so the caller can say a different choice was tried and went wrong.
    /// </para>
    /// <para>
    /// One rule for the command and the page, so refining the same idea from
    /// each never quietly switches model, which would change both what a
    /// round costs and how the plan is written.
    /// </para>
    /// <para>
    /// A model belongs to an agent. Naming only a model keeps the last round's
    /// agent; naming a different agent drops the last round's model, which
    /// that agent may not know.
    /// </para>
    /// </remarks>
    public static IdeaChoice Choose(IdeaRecord record, string? agent, string? model)
    {
        ArgumentNullException.ThrowIfNull(record);

        var asked = agent is { Length: > 0 } ? agent : null;
        var askedModel = model is { Length: > 0 } ? model : null;
        var last = record.LastRound;
        var lastAgent = last?.Agent is { Length: > 0 } a ? a : null;
        var lastModel = last?.Model is { Length: > 0 } m ? m : null;

        var chosenAgent = asked ?? lastAgent;

        var chosenModel = askedModel
            ?? (asked is null || string.Equals(asked, lastAgent, StringComparison.Ordinal) ? lastModel : null);

        return new IdeaChoice(
            chosenAgent,
            chosenModel,
            asked is null && askedModel is null && last is not null,
            FailureOf(record));
    }

    /// <summary>
    /// Whether the person has said anything since the plan that the agent has
    /// not yet seen: a request, something to improve, answers to questions
    /// asked after the plan was made, or a note no round has read.
    /// </summary>
    /// <remarks>
    /// Keep, drop and a changed choice are not new in this sense. They are the
    /// person's decisions about the plan, taken when it is accepted, and asking
    /// the agent to revise for them would spend a round to be told what was
    /// already decided.
    /// </remarks>
    private static bool AnythingNewFor(IdeaRecord record, IdeaPlan plan) =>
        record.Request.Length > 0
        || plan.Layers.Any(layer => layer.Verdict == IdeaVerdict.Improve)
        || plan.Additions.Any(addition => addition.Verdict == IdeaVerdict.Improve)
        || record.Rounds.Any(round => round.Revision >= plan.Revision)
        || record.Notes.Any(note => note.TakenUtc is null);

    /// <summary>
    /// Makes a question's recommendation one of its options, so it can be
    /// picked rather than retyped: an exact match ignoring case and spaces,
    /// then the longest option that starts the recommendation or that the
    /// recommendation starts, ending at a word. A tie would be a guess, so it
    /// counts as no match, and a recommendation that matches nothing is added
    /// as an option. Words left over after a matched option are the agent's
    /// reasoning, kept in Why.
    /// </summary>
    /// <remarks>
    /// The Studio Ideas page applies the same rule when it shows a question,
    /// for ideas saved before this ran. Both are tested against
    /// tests/Loadout.Tests/Fixtures/recommendations.json.
    /// </remarks>
    private static IdeaQuestion Recommend(IdeaQuestion question)
    {
        var wanted = Collapse(question.Recommendation);

        if (wanted.Length == 0)
        {
            question.Recommendation = string.Empty;
            return question;
        }

        var exact = question.Options.FindIndex(option => string.Equals(Collapse(option), wanted, StringComparison.OrdinalIgnoreCase));

        if (exact >= 0)
        {
            question.Recommendation = question.Options[exact];
            return question;
        }

        var best = -1;
        var length = 0;
        var tied = false;

        for (var index = 0; index < question.Options.Count; index++)
        {
            var option = Collapse(question.Options[index]);
            var shared = StartsAtWord(wanted, option) ? option.Length : StartsAtWord(option, wanted) ? wanted.Length : 0;

            if (shared > length)
            {
                (best, length, tied) = (index, shared, false);
            }
            else if (shared > 0 && shared == length)
            {
                tied = true;
            }
        }

        if (best < 0 || tied)
        {
            question.Options.Add(wanted);
            question.Recommendation = wanted;
            return question;
        }

        var rest = wanted[Math.Min(Collapse(question.Options[best]).Length, wanted.Length)..].TrimStart(' ', ',', ';', ':', '.', '-', '–', '—');

        if (rest.Length > 0 && !question.Why.Contains(rest, StringComparison.OrdinalIgnoreCase))
        {
            question.Why = question.Why.Length == 0 ? $"Recommended: {wanted}." : $"{question.Why} Recommended: {wanted}.";
        }

        question.Recommendation = question.Options[best];
        return question;
    }

    private static string Collapse(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>True when the shorter starts the longer and ends at a word.</summary>
    private static bool StartsAtWord(string longer, string shorter) =>
        longer.StartsWith(shorter, StringComparison.OrdinalIgnoreCase)
        && (longer.Length == shorter.Length || !char.IsLetterOrDigit(longer[shorter.Length]));

    /// <summary>
    /// Folds an agent's answer into the record: new questions become a round,
    /// a plan replaces the last one, carrying over what the person decided
    /// about each piece that is still there. The notes named in
    /// <paramref name="seen"/> are marked taken in only when the reply
    /// carries a plan: a round that only asked questions has not put them in
    /// a plan, so the next plan round must still be given them as unread.
    /// </summary>
    /// <remarks>
    /// Only those named, because a round reads the record when it starts and
    /// its answer lands minutes later. A note added in between was never in
    /// front of the agent, and marking it taken would lose it.
    /// </remarks>
    public static void Merge(IdeaRecord record, IdeaReply reply, DateTimeOffset now, IReadOnlyCollection<string>? seen = null)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(reply);

        if (reply.Questions.Count > 0)
        {
            var asked = record.Rounds.Sum(round => round.Questions.Count);

            record.Rounds.Add(new IdeaRound
            {
                AskedUtc = now,
                Revision = reply.Plan is null ? record.Plan?.Revision ?? 0 : (record.Plan?.Revision ?? 0) + 1,
                Questions =
                [
                    .. reply.Questions.Select((q, index) => Recommend(new IdeaQuestion
                    {
                        Id = $"Q{asked + index + 1}",
                        Question = q.Question.Trim(),
                        Why = q.Why.Trim(),
                        Options = [.. q.Options.Select(o => o.Trim()).Where(o => o.Length > 0)],
                        Recommendation = q.Recommendation.Trim(),
                    })),
                ],
            });
        }

        if (reply.Plan is { } proposed)
        {
            var previous = record.Plan;

            var plan = new IdeaPlan
            {
                Revision = (previous?.Revision ?? 0) + 1,
                ProposedUtc = now,
                Title = proposed.Title.Trim(),
                Understanding = reply.Understanding.Trim(),
                Project = new IdeaPlacement
                {
                    Slug = proposed.Project.Slug.Trim(),
                    IsNew = proposed.Project.IsNew,
                    Name = proposed.Project.Name.Trim(),
                    Reason = proposed.Project.Reason.Trim(),
                },
            };

            foreach (var (layer, index) in proposed.Layers.Select((l, i) => (l, i)))
            {
                var id = $"L{index + 1}";

                var made = new IdeaLayer
                {
                    Id = id,
                    Name = layer.Name.Trim(),
                    Purpose = layer.Purpose.Trim(),
                    Options =
                    [
                        .. layer.Options.Select((option, at) => new IdeaOption
                        {
                            Id = $"{id}{Letter(at)}",
                            Title = option.Title.Trim(),
                            Detail = option.Detail.Trim(),
                            Pros = [.. option.Pros.Select(p => p.Trim())],
                            Cons = [.. option.Cons.Select(c => c.Trim())],
                            Recommended = option.Recommended,
                        }),
                    ],
                };

                made.Chosen = (made.Options.FirstOrDefault(o => o.Recommended) ?? made.Options.FirstOrDefault())?.Id
                    ?? string.Empty;

                // Carried by name, because the ids are positions and a revision
                // is free to reorder. What was kept or dropped stays so; an
                // improvement has been asked for and answered, so it is not.
                if (previous?.Layers.FirstOrDefault(old => SameName(old.Name, made.Name)) is { } before)
                {
                    if (before.Verdict is IdeaVerdict.Keep or IdeaVerdict.Drop)
                    {
                        made.Verdict = before.Verdict;
                    }

                    var chosenBefore = before.Options.FirstOrDefault(o => o.Id == before.Chosen)?.Title;

                    if (made.Options.FirstOrDefault(o => SameName(o.Title, chosenBefore)) is { } same)
                    {
                        made.Chosen = same.Id;
                    }
                }

                plan.Layers.Add(made);
            }

            foreach (var (addition, index) in proposed.Additions.Select((a, i) => (a, i)))
            {
                var made = new IdeaAddition
                {
                    Id = $"A{index + 1}",
                    Title = addition.Title.Trim(),
                    Why = addition.Why.Trim(),
                };

                if (previous?.Additions.FirstOrDefault(old => SameName(old.Title, made.Title)) is { } before
                    && before.Verdict is IdeaVerdict.Keep or IdeaVerdict.Drop)
                {
                    made.Verdict = before.Verdict;
                }

                plan.Additions.Add(made);
            }

            record.Plan = plan;

            // Answered by this plan, so no longer outstanding.
            record.Request = string.Empty;

            if (seen is { Count: > 0 })
            {
                foreach (var note in record.Notes.Where(note =>
                             note.TakenUtc is null && seen.Contains(note.Id, StringComparer.OrdinalIgnoreCase)))
                {
                    note.TakenUtc = now;
                }
            }
        }

        ClearFailure(record);
    }

    private static string Letter(int index) =>
        index < 26
            ? ((char)('a' + index)).ToString()
            : (index + 1).ToString(CultureInfo.InvariantCulture);

    private static bool SameName(string? left, string? right) =>
        left is not null && right is not null
        && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Everything the agent is told for one round. The whole state goes every
    /// time, because every round is a fresh agent.
    /// </summary>
    public static string Prompt(
        IdeaRecord record,
        IReadOnlyList<IdeaProjectHint> projects,
        string? targetDescription)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(projects);

        var text = new StringBuilder();

        text.AppendLine(
            "You are helping somebody flesh out an idea before any work on it starts. Do not write "
            + "code or change any file in this session. Read whatever helps, then give your answer "
            + $"in the {IdeaSchema.Version} shape you have been given.");
        text.AppendLine();
        text.AppendLine("## The idea, as they wrote it");
        text.AppendLine();
        text.AppendLine(record.Ask.Trim());
        text.AppendLine();

        if (targetDescription is { Length: > 0 })
        {
            text.AppendLine(targetDescription);
            text.AppendLine();
        }

        text.AppendLine("## Projects they already have");
        text.AppendLine();

        if (projects.Count == 0)
        {
            text.AppendLine("None registered.");
        }
        else
        {
            foreach (var project in projects)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"- {project.Slug}: {project.Name}");
            }
        }

        text.AppendLine();
        text.AppendLine(
            "When you propose a plan, say where it belongs in plan.project: the slug of one of these "
            + "when it is work on that project, or is_new with a short name when it is a project of "
            + "its own. Leave the slug empty and is_new false when you cannot tell, and say why in the "
            + "reason.");
        text.AppendLine();

        AppendHistory(text, record);

        text.AppendLine("## This round");
        text.AppendLine();
        text.AppendLine(Instruction(record));
        text.AppendLine();
        text.AppendLine(
            "Layers are the architectural and technical layers that matter for this idea: for example "
            + "the interface, application logic, data and storage, integrations, hosting and "
            + "deployment, security, testing and observability. Name only those that apply, give each "
            + "two or three real options with their pros and cons, and mark the one you recommend. "
            + "Additions are things worth including that were not asked for, each with why. "
            + "understanding is what you now take the idea to be, in a few plain sentences.");

        return text.ToString();
    }

    private static string Instruction(IdeaRecord record)
    {
        if (record.Plan is null && record.Rounds.Count == 0)
        {
            return "Ask the clarifying questions whose answers would change the design, and nothing "
                + "you can reasonably decide yourself. At most eight, each with why it matters, the "
                + "answers you think likely, and the one you would recommend. The recommendation must be "
                + "one of those answers, word for word, with any reasoning in why. Do not propose a plan "
                + "yet, unless the idea is already so clear that no answer would change it.";
        }

        if (record.Plan is null)
        {
            return "Their answers are above. Propose the plan now. Ask further questions only where "
                + "an answer is essential; you may ask them alongside the plan.";
        }

        return "Revise the plan. Keep what they marked keep as it is, leave out what they dropped, "
            + "rework what they asked to improve as they asked, and apply any request about the "
            + "whole plan, and take in anything under What they have added since. Keep a layer's name the same where the layer itself is unchanged, so "
            + "their decisions about it carry over.";
    }

    private static void AppendHistory(StringBuilder text, IdeaRecord record)
    {
        var questions = record.Rounds.SelectMany(round => round.Questions).ToList();

        if (questions.Count > 0)
        {
            text.AppendLine("## Questions asked so far, and their answers");
            text.AppendLine();

            foreach (var question in questions)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"{question.Id}. {question.Question}");
                text.AppendLine(question.Answer.Length > 0
                    ? $"   Answer: {question.Answer}"
                    : "   Not answered.");
            }

            text.AppendLine();
        }

        // Both, every round: a fresh agent not shown what was taken in last
        // time would quietly write it out of the next revision.
        AppendNotes(text, "## What they have added since", record.Notes.Where(note => note.TakenUtc is null));
        AppendNotes(text, "## What they added earlier", record.Notes.Where(note => note.TakenUtc is not null));

        if (record.Plan is not { } plan)
        {
            return;
        }

        text.AppendLine(CultureInfo.InvariantCulture,
            $"## Your last proposal (revision {plan.Revision}), and what they made of it");
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"Title: {plan.Title}");
        text.AppendLine(CultureInfo.InvariantCulture, $"Understanding: {plan.Understanding}");
        text.AppendLine();

        foreach (var layer in plan.Layers)
        {
            var chosen = layer.Options.FirstOrDefault(o => o.Id == layer.Chosen);

            text.AppendLine(CultureInfo.InvariantCulture,
                $"- Layer '{layer.Name}': options {string.Join(", ", layer.Options.Select(o => $"\"{o.Title}\""))}; "
                + $"chosen \"{chosen?.Title ?? "none"}\"; {Said(layer.Verdict, layer.Request)}");
        }

        foreach (var addition in plan.Additions)
        {
            text.AppendLine(CultureInfo.InvariantCulture,
                $"- Addition '{addition.Title}': {Said(addition.Verdict, addition.Request)}");
        }

        if (record.Request.Length > 0)
        {
            text.AppendLine();
            text.AppendLine(CultureInfo.InvariantCulture, $"About the whole plan they asked: {record.Request}");
        }

        text.AppendLine();
    }

    private static void AppendNotes(StringBuilder text, string heading, IEnumerable<IdeaNote> notes)
    {
        var listed = notes.ToList();

        if (listed.Count == 0)
        {
            return;
        }

        text.AppendLine(heading);
        text.AppendLine();

        foreach (var note in listed)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"{note.Id} (by {note.By}, {Day(note.AddedUtc)}): {note.Text}");
        }

        text.AppendLine();
    }

    private static string Day(DateTimeOffset utc) =>
        utc.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Said(IdeaVerdict verdict, string request) => verdict switch
    {
        IdeaVerdict.Keep => "keep",
        IdeaVerdict.Drop => "drop",
        IdeaVerdict.Improve => $"improve: {request}",
        _ => "no decision yet",
    };

    /// <summary>
    /// Where an accepted idea goes when the person has not said: the project
    /// it was dropped into, or the one the agent placed it on.
    /// </summary>
    /// <remarks>
    /// What the person did outranks what the agent guessed. Dropping an idea
    /// into a project's list is saying where it belongs, so an agent that thinks
    /// otherwise is not followed; the disagreement is said, and the person
    /// settles it. The agent's guess is taken only for an idea put on no
    /// project, and only when it names one that is registered.
    /// </remarks>
    public static IdeaDestination Destination(
        IdeaPlace place,
        IdeaPlan plan,
        IReadOnlyCollection<string> registered)
    {
        ArgumentNullException.ThrowIfNull(place);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(registered);

        var guess = plan.Project;

        if (place.Project is { Length: > 0 } dropped)
        {
            if (guess.IsNew)
            {
                return new IdeaDestination(null, null,
                    $"It is on {dropped}, but the agent thinks it is a project of its own"
                    + (guess.Name.Length > 0 ? $" ('{guess.Name}')" : string.Empty)
                    + $": {guess.Reason} Say which.");
            }

            if (guess.Slug.Length > 0 && !string.Equals(guess.Slug, dropped, StringComparison.OrdinalIgnoreCase))
            {
                return new IdeaDestination(null, null,
                    $"It is on {dropped}, but the agent placed it on {guess.Slug}: {guess.Reason} Say which.");
            }

            return new IdeaDestination(dropped, null, $"It was put on {dropped}.");
        }

        if (guess.Slug.Length > 0
            && registered.FirstOrDefault(slug => string.Equals(slug, guess.Slug, StringComparison.OrdinalIgnoreCase))
                is { } known)
        {
            return new IdeaDestination(known, null, $"The agent placed it on {known}: {guess.Reason}");
        }

        if (guess.IsNew)
        {
            return new IdeaDestination(null, null,
                "The agent thinks it is a project of its own"
                + (guess.Name.Length > 0 ? $", called '{guess.Name}'" : string.Empty)
                + ". Making one is yours to decide.");
        }

        return new IdeaDestination(null, null,
            guess.Slug.Length > 0
                ? $"The agent placed it on '{guess.Slug}', which is not a registered project."
                : "Neither you nor the agent has said which project it belongs to.");
    }

    /// <summary>The plan as it will be handed on: only what the person kept, in the choices they made.</summary>
    /// <remarks>
    /// A layer nobody decided about is in, with the option chosen for it,
    /// because the agent proposed it as part of the design. An addition nobody
    /// decided about is out, because it was offered as extra, and extra is
    /// something a person opts into.
    /// </remarks>
    public static string Document(IdeaRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var plan = record.Plan
            ?? throw new InvalidOperationException("An idea with no plan has no document.");

        var text = new StringBuilder();

        text.AppendLine(CultureInfo.InvariantCulture, $"# {plan.Title}");
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture,
            $"From the idea '{record.Id}', revision {plan.Revision} of its plan.");
        text.AppendLine();
        text.AppendLine("## What was asked");
        text.AppendLine();
        text.AppendLine(record.Ask.Trim());
        text.AppendLine();

        foreach (var note in record.Notes)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"Added later, by {note.By} on {Day(note.AddedUtc)}:");
            text.AppendLine();
            text.AppendLine(note.Text);
            text.AppendLine();
        }

        text.AppendLine("## What it is");
        text.AppendLine();
        text.AppendLine(plan.Understanding);
        text.AppendLine();

        var answered = record.Rounds.SelectMany(r => r.Questions).Where(q => q.Answer.Length > 0).ToList();

        if (answered.Count > 0)
        {
            text.AppendLine("## Decided along the way");
            text.AppendLine();

            foreach (var question in answered)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"- {question.Question} **{question.Answer}**");
            }

            text.AppendLine();
        }

        text.AppendLine("## Design");
        text.AppendLine();

        foreach (var layer in plan.Layers.Where(l => l.Verdict != IdeaVerdict.Drop))
        {
            var chosen = layer.Options.FirstOrDefault(o => o.Id == layer.Chosen);

            text.AppendLine(CultureInfo.InvariantCulture, $"### {layer.Name}");
            text.AppendLine();

            if (layer.Purpose.Length > 0)
            {
                text.AppendLine(layer.Purpose);
                text.AppendLine();
            }

            if (chosen is null)
            {
                text.AppendLine("No option chosen.");
                text.AppendLine();
                continue;
            }

            text.AppendLine(CultureInfo.InvariantCulture, $"**{chosen.Title}.** {chosen.Detail}");
            text.AppendLine();

            foreach (var pro in chosen.Pros)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"- For: {pro}");
            }

            foreach (var con in chosen.Cons)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"- Against: {con}");
            }

            var passed = layer.Options.Where(o => o.Id != chosen.Id).Select(o => o.Title).ToList();

            if (passed.Count > 0)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"- Considered instead: {string.Join(", ", passed)}");
            }

            text.AppendLine();
        }

        var kept = plan.Additions.Where(a => a.Verdict == IdeaVerdict.Keep).ToList();

        if (kept.Count > 0)
        {
            text.AppendLine("## Also included");
            text.AppendLine();

            foreach (var addition in kept)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"- **{addition.Title}.** {addition.Why}");
            }

            text.AppendLine();
        }

        var dropped = plan.Layers.Where(l => l.Verdict == IdeaVerdict.Drop).Select(l => l.Name)
            .Concat(plan.Additions.Where(a => a.Verdict == IdeaVerdict.Drop).Select(a => a.Title))
            .ToList();

        if (dropped.Count > 0)
        {
            text.AppendLine("## Left out on purpose");
            text.AppendLine();

            foreach (var name in dropped)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"- {name}");
            }

            text.AppendLine();
        }

        return text.ToString();
    }
}
