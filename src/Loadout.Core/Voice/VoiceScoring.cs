using System.Security.Cryptography;
using System.Text;
using Loadout.Models.Voice;

namespace Loadout.Core.Voice;

/// <summary>
/// Scoring rules against writing they were not learned from: the person's
/// own, held back from the miner, and descriptions they rewrote when they
/// merged.
/// </summary>
/// <remarks>
/// <para>
/// Only typed rules are scored here, by the same checker the audit uses, so a
/// score is something anybody can reproduce. A judged rule needs a model to
/// say whether a sample follows it, and is recorded as unscored with that
/// reason rather than given a number.
/// </para>
/// <para>
/// An edit pair counts for a rule only when the rule has an opinion on it,
/// firing on the draft, on what was kept, or both. A rule about headings has
/// nothing to say about a pull request with none, and counting that as
/// agreement would inflate every score with pairs that test nothing.
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

    /// <summary>Scores each rule against the held-out writing and the edit pairs.</summary>
    /// <param name="rules">The rules to score: the profile as it stands after the run.</param>
    /// <param name="heldOut">The person's own writing that was not mined.</param>
    /// <param name="pairs">Descriptions the person rewrote, with what they kept.</param>
    /// <param name="guide">Whose thresholds.</param>
    public static VoiceScore Score(
        IReadOnlyList<VoiceRule> rules,
        IReadOnlyList<VoiceEvidence> heldOut,
        IReadOnlyList<VoiceEditPair> pairs,
        VoiceGuide guide)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(heldOut);
        ArgumentNullException.ThrowIfNull(pairs);
        ArgumentNullException.ThrowIfNull(guide);

        var score = new VoiceScore { Own = heldOut.Count, Edits = pairs.Count };

        foreach (var rule in rules)
        {
            score.Rules.Add(ScoreRule(rule, heldOut, pairs, guide));
        }

        return score;
    }

    private static VoiceRuleScore ScoreRule(
        VoiceRule rule,
        IReadOnlyList<VoiceEvidence> heldOut,
        IReadOnlyList<VoiceEditPair> pairs,
        VoiceGuide guide)
    {
        var result = new VoiceRuleScore { Rule = rule.Id, Check = rule.Check?.Type };

        if (rule.Check is null)
        {
            result.Unscored = "a judged rule needs a model to score it, and this version scores only typed checks";

            return result;
        }

        if (!guide.CheckTypes.Contains(rule.Check.Type, StringComparer.OrdinalIgnoreCase))
        {
            result.Unscored = $"'{rule.Check.Type}' is not a check type {guide.Reference} knows";

            return result;
        }

        foreach (var piece in heldOut)
        {
            var surface = Surface(piece.Source);

            if (!Applies(rule, surface))
            {
                continue;
            }

            result.Own++;

            if (!Fires(rule, piece.Text, surface))
            {
                result.OwnAgreed++;
            }
        }

        // Both sides are read as commit messages: a pull request's title
        // becomes the squash subject, so a subject rule reads the two alike.
        if (Applies(rule, VoiceGuides.Commit))
        {
            foreach (var pair in pairs)
            {
                var draft = Fires(rule, pair.Before, VoiceGuides.Commit);
                var kept = Fires(rule, pair.After, VoiceGuides.Commit);

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

        var samples = result.Own + result.Edit;

        if (samples == 0)
        {
            result.Unscored = "no held-out sample it applies to";

            return result;
        }

        result.Agreement = Math.Round((double)(result.OwnAgreed + result.EditAgreed) / samples, 4);

        // A regex reports and never gates, so it can never be promoted.
        result.Meets = result.Agreement >= guide.Promotion.Agreement
            && result.Edit >= guide.Promotion.EditFloor
            && !string.Equals(rule.Check.Type, VoiceChecker.Regex, StringComparison.OrdinalIgnoreCase);

        return result;
    }

    private static bool Applies(VoiceRule rule, string surface) =>
        rule.Surfaces.Count == 0 || rule.Surfaces.Contains(surface, StringComparer.OrdinalIgnoreCase);

    private static bool Fires(VoiceRule rule, string text, string surface) =>
        VoiceChecker.Check(text, surface, [rule]).Findings.Any(f => string.Equals(f.Rule, rule.Id, StringComparison.Ordinal));
}
