using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Loadout.Models.Voice;

namespace Loadout.Core.Voice;

/// <summary>What a judging round said, and about which samples.</summary>
/// <param name="Samples">The samples it was shown. Anything else was not judged and is not counted.</param>
/// <param name="Broken">Each rule and sample it judged broken.</param>
public sealed record VoiceJudgement(ISet<string> Samples, ISet<(string Rule, string Sample)> Broken);

/// <summary>
/// Scoring rules against writing they were not learned from: the person's
/// own, held back from the miner; descriptions they rewrote when they
/// merged; and variants of their own writing rewritten to break a rule.
/// </summary>
/// <remarks>
/// <para>
/// Typed rules are scored by the same checker the audit uses, so that part of
/// a score is something anybody can reproduce. A judged rule needs a model to
/// say whether a sample breaks it, and is scored only when a judging round was
/// asked and answered; otherwise it is recorded as unscored with the reason.
/// </para>
/// <para>
/// An edit pair counts for a rule only when the rule has an opinion on it,
/// broken in the draft, in what was kept, or both. A rule about headings has
/// nothing to say about a pull request with none, and counting that as
/// agreement would inflate every score with pairs that test nothing.
/// </para>
/// <para>
/// Variants fill the gap edits leave. A rule the history never breaks has no
/// edit to be tested on, so a model rewrites one of the person's held-out
/// pieces to break it, and the rule agrees when it catches the rewrite. They
/// are counted apart from edits, because a model's idea of breaking a rule is
/// not the person's, and promotion's floor is on edits alone.
/// </para>
/// </remarks>
public static class VoiceScoring
{
    /// <summary>
    /// Splits the writing into what the miner may read and what is held back
    /// for scoring.
    /// </summary>
    /// <remarks>
    /// Decided by a hash of each piece's reference rather than at random, so
    /// the same pieces are held back on every run. A piece mined one run and
    /// held back the next would let a rule be scored on its own evidence.
    /// </remarks>
    public static (IReadOnlyList<VoiceEvidence> Learn, IReadOnlyList<VoiceEvidence> HeldOut) HoldOut(
        IReadOnlyList<VoiceEvidence> evidence,
        VoiceGuide guide)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(guide);

        var learn = new List<VoiceEvidence>();
        var held = new List<VoiceEvidence>();

        foreach (var piece in evidence)
        {
            (IsHeldOut(piece.Reference, guide.Scoring.HoldOut) ? held : learn).Add(piece);
        }

        return (learn, held);
    }

    /// <summary>Whether a piece of writing is one of those held back, on every run.</summary>
    public static bool IsHeldOut(string reference, double share)
    {
        ArgumentNullException.ThrowIfNull(reference);

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(reference));
        var position = ((digest[0] << 8) | digest[1]) / 65536.0;

        return position < share;
    }

    /// <summary>The surface a piece of writing is, for deciding which rules apply to it.</summary>
    public static string Surface(string source) => source switch
    {
        VoiceGuides.Commit => VoiceGuides.Commit,
        VoiceGuides.PullRequest => VoiceGuides.PullRequest,
        _ => VoiceGuides.Document,
    };

    /// <summary>How many model calls scoring may make: one for variants and one for judging.</summary>
    /// <remarks>
    /// Counted into the estimate whenever there is anything to score against,
    /// before anybody knows whether the rules accepted will need either. An
    /// estimate that is sometimes two calls high is the price of a cap that
    /// is never asked for again halfway through.
    /// </remarks>
    public static int Calls(int heldOut, int pairs) => heldOut > 0 || pairs > 0 ? 2 : 0;

    /// <summary>
    /// The rules worth a variant, each with the held-out piece to rewrite: any
    /// rule short of the edit floor, judged or typed, that applies to a piece
    /// held back.
    /// </summary>
    public static IReadOnlyList<(VoiceRule Rule, VoiceSample Sample)> VariantRequests(
        IReadOnlyList<VoiceRule> rules,
        IReadOnlyList<VoiceEvidence> heldOut,
        IReadOnlyList<VoiceEditPair> pairs,
        VoiceGuide guide)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(heldOut);
        ArgumentNullException.ThrowIfNull(pairs);
        ArgumentNullException.ThrowIfNull(guide);

        var requests = new List<(VoiceRule, VoiceSample)>();

        foreach (var rule in rules)
        {
            if (rule.Check is not null && Edits(rule, pairs, Fires) >= guide.Promotion.EditFloor)
            {
                continue;
            }

            if (heldOut.FirstOrDefault(p => Applies(rule, Surface(p.Source))) is { } piece)
            {
                requests.Add((rule, new VoiceSample(piece.Id, VoiceJudging.Clip(piece.Text))));
            }
        }

        return requests;
    }

    /// <summary>
    /// The samples a judging round is shown for the judged rules: variants
    /// first, since nothing else tests them, then both sides of each edit pair,
    /// then the held-out writing, until the round is full.
    /// </summary>
    public static IReadOnlyList<VoiceSample> JudgeSamples(
        IReadOnlyList<VoiceRule> judged,
        IReadOnlyList<VoiceEvidence> heldOut,
        IReadOnlyList<VoiceEditPair> pairs,
        IReadOnlyList<VoiceVariant> variants,
        VoiceGuide guide)
    {
        ArgumentNullException.ThrowIfNull(judged);
        ArgumentNullException.ThrowIfNull(heldOut);
        ArgumentNullException.ThrowIfNull(pairs);
        ArgumentNullException.ThrowIfNull(variants);
        ArgumentNullException.ThrowIfNull(guide);

        var ids = judged.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        var candidates = new List<VoiceSample>();

        for (var i = 0; i < variants.Count; i++)
        {
            if (ids.Contains(variants[i].Rule))
            {
                candidates.Add(new VoiceSample(VariantId(i), variants[i].Text));
            }
        }

        if (judged.Any(r => Applies(r, VoiceGuides.Commit)))
        {
            for (var i = 0; i < pairs.Count; i++)
            {
                candidates.Add(new VoiceSample(DraftId(i), VoiceJudging.Clip(pairs[i].Before)));
                candidates.Add(new VoiceSample(KeptId(i), VoiceJudging.Clip(pairs[i].After)));
            }
        }

        candidates.AddRange(heldOut
            .Where(p => judged.Any(r => Applies(r, Surface(p.Source))))
            .Select(p => new VoiceSample(p.Id, VoiceJudging.Clip(p.Text))));

        var samples = new List<VoiceSample>();
        var size = 0;

        foreach (var sample in candidates)
        {
            if (size + sample.Text.Length > guide.Cap.BatchCharacters)
            {
                // A pair is judged whole or not at all: half of one has no
                // opinion to count.
                if (sample.Id.StartsWith('k') && samples.Count > 0 && samples[^1].Id.StartsWith('d'))
                {
                    size -= samples[^1].Text.Length;
                    samples.RemoveAt(samples.Count - 1);
                }

                break;
            }

            samples.Add(sample);
            size += sample.Text.Length;
        }

        return samples;
    }

    /// <summary>Scores each rule against everything held back.</summary>
    /// <param name="rules">The rules to score: the profile as it stands after the run.</param>
    /// <param name="heldOut">The person's own writing that was not mined.</param>
    /// <param name="pairs">Descriptions the person rewrote, with what they kept.</param>
    /// <param name="guide">Whose thresholds.</param>
    /// <param name="variants">Held-out writing rewritten to break a rule.</param>
    /// <param name="judgement">What the judging round said, or null when there was none.</param>
    /// <param name="unjudged">Why there was no judging round, for the judged rules' records.</param>
    public static VoiceScore Score(
        IReadOnlyList<VoiceRule> rules,
        IReadOnlyList<VoiceEvidence> heldOut,
        IReadOnlyList<VoiceEditPair> pairs,
        VoiceGuide guide,
        IReadOnlyList<VoiceVariant>? variants = null,
        VoiceJudgement? judgement = null,
        string? unjudged = null)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(heldOut);
        ArgumentNullException.ThrowIfNull(pairs);
        ArgumentNullException.ThrowIfNull(guide);

        variants ??= [];

        var score = new VoiceScore { Own = heldOut.Count, Edits = pairs.Count, Variants = variants.Count };

        foreach (var rule in rules)
        {
            score.Rules.Add(ScoreRule(rule, heldOut, pairs, variants, guide, judgement, unjudged));
        }

        return score;
    }

    private static VoiceRuleScore ScoreRule(
        VoiceRule rule,
        IReadOnlyList<VoiceEvidence> heldOut,
        IReadOnlyList<VoiceEditPair> pairs,
        IReadOnlyList<VoiceVariant> variants,
        VoiceGuide guide,
        VoiceJudgement? judgement,
        string? unjudged)
    {
        var result = new VoiceRuleScore { Rule = rule.Id, Check = rule.Check?.Type };

        // How this rule decides whether a sample breaks it, given the sample's
        // text, its surface and its id, or null when nothing can say.
        Func<string, string, string, bool>? breaks;

        if (rule.Check is null)
        {
            breaks = judgement is null
                ? null
                : (_, _, id) => judgement.Broken.Contains((rule.Id, id));
        }
        else if (!guide.CheckTypes.Contains(rule.Check.Type, StringComparer.OrdinalIgnoreCase))
        {
            result.Unscored = $"'{rule.Check.Type}' is not a check type {guide.Reference} knows";

            return result;
        }
        else
        {
            breaks = (text, surface, _) => Fires(rule, text, surface);
        }

        if (breaks is null)
        {
            result.Unscored = unjudged ?? "a judged rule needs a model to score it, and none was asked";

            return result;
        }

        // A judged rule counts only what the round was shown.
        bool Seen(string id) => rule.Check is not null || judgement!.Samples.Contains(id);

        foreach (var piece in heldOut)
        {
            var surface = Surface(piece.Source);

            if (!Applies(rule, surface) || !Seen(piece.Id))
            {
                continue;
            }

            result.Own++;

            if (!breaks(piece.Text, surface, piece.Id))
            {
                result.OwnAgreed++;
            }
        }

        // Both sides are read as commit messages: a pull request's title
        // becomes the squash subject, so a subject rule reads the two alike.
        if (Applies(rule, VoiceGuides.Commit))
        {
            for (var i = 0; i < pairs.Count; i++)
            {
                if (!Seen(DraftId(i)) || !Seen(KeptId(i)))
                {
                    continue;
                }

                var draft = breaks(pairs[i].Before, VoiceGuides.Commit, DraftId(i));
                var kept = breaks(pairs[i].After, VoiceGuides.Commit, KeptId(i));

                if (!draft && !kept)
                {
                    continue;
                }

                result.Edit++;

                if (draft && !kept)
                {
                    result.EditAgreed++;
                }
            }
        }

        for (var i = 0; i < variants.Count; i++)
        {
            if (!string.Equals(variants[i].Rule, rule.Id, StringComparison.Ordinal) || !Seen(VariantId(i)))
            {
                continue;
            }

            var surface = heldOut.FirstOrDefault(p => p.Id == variants[i].Sample) is { } from
                ? Surface(from.Source)
                : VoiceGuides.Document;

            result.Generated++;

            if (breaks(variants[i].Text, surface, VariantId(i)))
            {
                result.GeneratedAgreed++;
            }
        }

        var samples = result.Own + result.Edit + result.Generated;

        if (samples == 0)
        {
            result.Unscored = "no held-out sample it applies to";

            return result;
        }

        result.Agreement = Math.Round(
            (double)(result.OwnAgreed + result.EditAgreed + result.GeneratedAgreed) / samples, 4);

        // Only a typed rule can gate, and a regex never does, so only those
        // can meet the promotion thresholds.
        result.Meets = result.Agreement >= guide.Promotion.Agreement
            && result.Edit >= guide.Promotion.EditFloor
            && rule.Check is not null
            && !string.Equals(rule.Check.Type, VoiceChecker.Regex, StringComparison.OrdinalIgnoreCase);

        return result;
    }

    private static int Edits(VoiceRule rule, IReadOnlyList<VoiceEditPair> pairs, Func<VoiceRule, string, string, bool> fires) =>
        Applies(rule, VoiceGuides.Commit)
            ? pairs.Count(p => fires(rule, p.Before, VoiceGuides.Commit) || fires(rule, p.After, VoiceGuides.Commit))
            : 0;

    private static string DraftId(int index) => "d" + (index + 1).ToString(CultureInfo.InvariantCulture);

    private static string KeptId(int index) => "k" + (index + 1).ToString(CultureInfo.InvariantCulture);

    private static string VariantId(int index) => "v" + (index + 1).ToString(CultureInfo.InvariantCulture);

    private static bool Applies(VoiceRule rule, string surface) =>
        rule.Surfaces.Count == 0 || rule.Surfaces.Contains(surface, StringComparer.OrdinalIgnoreCase);

    private static bool Fires(VoiceRule rule, string text, string surface) =>
        VoiceChecker.Check(text, surface, [rule]).Findings.Any(f => string.Equals(f.Rule, rule.Id, StringComparison.Ordinal));
}
