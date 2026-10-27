using System.Globalization;
using System.Text.RegularExpressions;
using Loadout.Models.Ideas;

namespace Loadout.Core.Ideas;

/// <summary>What a question is about, as every surface shows it.</summary>
/// <param name="Label">What it relates to, readably: <c>L2 Storage</c>, <c>N3</c>, <c>the ask</c>; or null.</param>
/// <param name="Context">The agent's line of context, or empty.</param>
/// <param name="Outdated">Why what it relates to may have changed since, or null when it has not.</param>
public sealed record IdeaQuestionContext(string? Label, string Context, string? Outdated)
{
    /// <summary>
    /// <c>Relates to L2 Storage: why it is asked</c>, the context alone, the
    /// reference alone, or null when there is neither.
    /// </summary>
    public string? Line => (Label, Context.Length > 0) switch
    {
        ({ } label, true) => $"Relates to {label}: {Context}",
        ({ } label, false) => $"Relates to {label}",
        (null, true) => Context,
        _ => null,
    };
}

public static partial class IdeaWork
{
    /// <summary>The reference that names the idea as it was asked.</summary>
    public const string TheAsk = "ask";

    /// <summary>
    /// What a question is about and whether that has changed since, worked out
    /// from the record each time rather than stored, the way the stage is, so
    /// it can never disagree with what is there.
    /// </summary>
    public static IdeaQuestionContext About(IdeaRecord record, IdeaQuestion question)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(question);

        return new IdeaQuestionContext(Label(question), question.Context, Outdated(record, question));
    }

    /// <summary>
    /// Why what a question relates to may have changed since it was asked or
    /// last answered, such as <c>N2 was removed</c> or <c>layer Storage
    /// changed in revision 3</c>, or null when it has not. The ask is never
    /// rewritten, so a question about it is never outdated; a layer only moved
    /// to another place in the plan is found by name and is not either.
    /// </summary>
    public static string? Outdated(IdeaRecord record, IdeaQuestion question)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(question);

        if (question.Snapshot is not { } then || Kind(question.RelatesTo) is not { } kind || kind == RefKind.Ask)
        {
            return null;
        }

        var now = Current(record, kind, question.RelatesTo, then);

        if (now is null)
        {
            return then.Missing ? null : Gone(record, kind, question.RelatesTo, then);
        }

        return !then.Missing && Same(now.Text, then.Text) ? null : Changed(record, kind, question.RelatesTo, then);
    }

    /// <summary>How many of the idea's questions may have been overtaken by a change, answered or not.</summary>
    public static int OutdatedCount(IdeaRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return record.Rounds.SelectMany(round => round.Questions).Count(question => Outdated(record, question) is not null);
    }

    /// <summary>
    /// Takes a fresh snapshot of what a question relates to, as it is now:
    /// answering it, or giving the same answer again, says the answer stands
    /// against what is there now, and clears its outdated marker.
    /// </summary>
    public static void Retake(IdeaRecord record, IdeaQuestion question)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(question);

        if (question.Snapshot is not { } then || Kind(question.RelatesTo) is not { } kind || kind == RefKind.Ask)
        {
            return;
        }

        question.Snapshot = Current(record, kind, question.RelatesTo, then) is { } now
            ? new IdeaSnapshot { Name = then.Name, Option = then.Option, Text = now.Text, Revision = now.Revision }
            : new IdeaSnapshot { Name = then.Name, Option = then.Option, Revision = record.Plan?.Revision ?? 0, Missing = true };
    }

    /// <summary>
    /// The reference an agent gave, made canonical and checked against the
    /// record, with what it names now: an id the record does not have is
    /// cleared rather than kept pointing at nothing.
    /// </summary>
    /// <param name="record">The record, with the plan the ids belong to already in it.</param>
    /// <param name="asked">Questions earlier in the same answer, which a question may also refer to.</param>
    /// <param name="given">What the agent wrote.</param>
    private static (string RelatesTo, IdeaSnapshot? Snapshot) Refer(
        IdeaRecord record,
        IReadOnlyList<IdeaQuestion> asked,
        string given)
    {
        var id = Canonical(given);

        if (id is null || Kind(id) is not { } kind)
        {
            return (string.Empty, null);
        }

        var plan = record.Plan;

        switch (kind)
        {
            case RefKind.Ask:
                return (id, null);

            case RefKind.Note:
                return record.Notes.FirstOrDefault(note => NoteNumber(note.Id) == NoteNumber(id)) is { } note
                    ? (note.Id, new IdeaSnapshot { Text = note.Text })
                    : (string.Empty, null);

            case RefKind.Question:
                return record.Rounds.SelectMany(round => round.Questions).Concat(asked)
                    .FirstOrDefault(q => string.Equals(q.Id, id, StringComparison.OrdinalIgnoreCase)) is { } earlier
                    ? (earlier.Id, new IdeaSnapshot { Text = QuestionText(earlier) })
                    : (string.Empty, null);

            case RefKind.Layer:
                return plan?.Layers.FirstOrDefault(l => string.Equals(l.Id, id, StringComparison.OrdinalIgnoreCase)) is { } layer
                    ? (layer.Id, new IdeaSnapshot { Name = layer.Name, Text = LayerText(layer), Revision = plan.Revision })
                    : (string.Empty, null);

            case RefKind.Option:
                var owner = plan?.Layers.FirstOrDefault(l => id.StartsWith(l.Id, StringComparison.OrdinalIgnoreCase)
                    && l.Options.Any(o => string.Equals(o.Id, id, StringComparison.OrdinalIgnoreCase)));
                var option = owner?.Options.First(o => string.Equals(o.Id, id, StringComparison.OrdinalIgnoreCase));

                return owner is not null && option is not null
                    ? (option.Id, new IdeaSnapshot { Name = owner.Name, Option = option.Title, Text = option.Detail, Revision = plan!.Revision })
                    : (string.Empty, null);

            default:
                return plan?.Additions.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase)) is { } addition
                    ? (addition.Id, new IdeaSnapshot { Name = addition.Title, Text = addition.Why, Revision = plan.Revision })
                    : (string.Empty, null);
        }
    }

    /// <summary>What the subject is now, found the way a revision carries things over: by name, not position.</summary>
    private static IdeaSnapshot? Current(IdeaRecord record, RefKind kind, string id, IdeaSnapshot then)
    {
        var plan = record.Plan;

        switch (kind)
        {
            case RefKind.Note:
                return record.Notes.FirstOrDefault(note => NoteNumber(note.Id) == NoteNumber(id)) is { } note
                    ? new IdeaSnapshot { Text = note.Text }
                    : null;

            case RefKind.Question:
                return record.Rounds.SelectMany(round => round.Questions)
                    .FirstOrDefault(q => string.Equals(q.Id, id, StringComparison.OrdinalIgnoreCase)) is { } earlier
                    ? new IdeaSnapshot { Text = QuestionText(earlier) }
                    : null;

            case RefKind.Layer:
                return plan?.Layers.FirstOrDefault(l => SameName(l.Name, then.Name)) is { } layer
                    ? new IdeaSnapshot { Text = LayerText(layer), Revision = plan.Revision }
                    : null;

            case RefKind.Option:
                return plan?.Layers.FirstOrDefault(l => SameName(l.Name, then.Name))?.Options
                    .FirstOrDefault(o => SameName(o.Title, then.Option)) is { } option
                    ? new IdeaSnapshot { Text = option.Detail, Revision = plan.Revision }
                    : null;

            case RefKind.Addition:
                return plan?.Additions.FirstOrDefault(a => SameName(a.Title, then.Name)) is { } addition
                    ? new IdeaSnapshot { Text = addition.Why, Revision = plan.Revision }
                    : null;

            default:
                return null;
        }
    }

    private static string Gone(IdeaRecord record, RefKind kind, string id, IdeaSnapshot then)
    {
        var revision = (record.Plan?.Revision ?? 0).ToString(CultureInfo.InvariantCulture);

        return kind switch
        {
            RefKind.Note => $"{id} was removed",
            RefKind.Question => $"{id} is no longer on the idea",
            RefKind.Layer => $"layer {then.Name} was dropped in revision {revision}",
            RefKind.Option => record.Plan?.Layers.Any(l => SameName(l.Name, then.Name)) == true
                ? $"option {then.Option} of layer {then.Name} was dropped in revision {revision}"
                : $"layer {then.Name} was dropped in revision {revision}",
            _ => $"addition {then.Name} was dropped in revision {revision}",
        };
    }

    private static string Changed(IdeaRecord record, RefKind kind, string id, IdeaSnapshot then)
    {
        var revision = (record.Plan?.Revision ?? 0).ToString(CultureInfo.InvariantCulture);

        return kind switch
        {
            RefKind.Note => $"{id} has changed",
            RefKind.Question => $"the answer to {id} has changed",
            RefKind.Layer => $"layer {then.Name} changed in revision {revision}",
            RefKind.Option => $"option {then.Option} of layer {then.Name} changed in revision {revision}",
            _ => $"addition {then.Name} changed in revision {revision}",
        };
    }

    /// <summary>What a question relates to, readably, from what was kept when it was asked.</summary>
    private static string? Label(IdeaQuestion question)
    {
        if (Kind(question.RelatesTo) is not { } kind)
        {
            return null;
        }

        var then = question.Snapshot;

        return kind switch
        {
            RefKind.Ask => "the ask",
            RefKind.Layer when then is { Name.Length: > 0 } => $"{question.RelatesTo} {then.Name}",
            RefKind.Option when then is { Option.Length: > 0 } => $"{question.RelatesTo} {then.Option} in {then.Name}",
            RefKind.Addition when then is { Name.Length: > 0 } => $"{question.RelatesTo} {then.Name}",
            _ => question.RelatesTo,
        };
    }

    private static string LayerText(IdeaLayer layer) =>
        string.Join('\n', [layer.Purpose, .. layer.Options.Select(option => option.Title)]);

    private static string QuestionText(IdeaQuestion question) => $"{question.Question}\n{question.Answer}";

    private static bool Same(string left, string right) =>
        string.Equals(Collapse(left), Collapse(right), StringComparison.Ordinal);

    private enum RefKind
    {
        Ask,
        Note,
        Layer,
        Option,
        Addition,
        Question,
    }

    /// <summary>
    /// A reference in the one spelling the record keeps: <c>ask</c>, <c>N2</c>,
    /// <c>L1</c>, <c>L1b</c>, <c>A2</c>, <c>Q3</c>. Null for anything else,
    /// including an empty one.
    /// </summary>
    private static string? Canonical(string? given)
    {
        var trimmed = (given ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            return null;
        }

        if (string.Equals(trimmed, TheAsk, StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "the ask", StringComparison.OrdinalIgnoreCase))
        {
            return TheAsk;
        }

        var match = Reference().Match(trimmed);

        if (!match.Success)
        {
            return null;
        }

        var prefix = char.ToUpperInvariant(match.Groups["kind"].Value[0]);
        var letter = match.Groups["option"].Value.ToLowerInvariant();

        return letter.Length > 0 && prefix != 'L'
            ? null
            : $"{prefix}{match.Groups["number"].Value.TrimStart('0')}{letter}";
    }

    private static RefKind? Kind(string id)
    {
        if (string.Equals(id, TheAsk, StringComparison.Ordinal))
        {
            return RefKind.Ask;
        }

        if (id.Length < 2)
        {
            return null;
        }

        return id[0] switch
        {
            'N' => RefKind.Note,
            'Q' => RefKind.Question,
            'A' => RefKind.Addition,
            'L' => char.IsLetter(id[^1]) ? RefKind.Option : RefKind.Layer,
            _ => null,
        };
    }

    [GeneratedRegex(@"^(?<kind>[NnLlAaQq])(?<number>0*[1-9][0-9]*)(?<option>[A-Za-z])?$", RegexOptions.CultureInvariant)]
    private static partial Regex Reference();
}
