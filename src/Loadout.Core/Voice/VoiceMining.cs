using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Loadout.Models.Results;
using Loadout.Models.Voice;

namespace Loadout.Core.Voice;

/// <summary>
/// What a mining round asks for and how its answer is read.
/// </summary>
/// <remarks>
/// <para>
/// The agent is handed the writing and asked what rules it follows, each with
/// the passages that show it. It reads; it decides nothing. Whether a rule is
/// put to the person as a proposal or as an interview question is settled
/// here, by counting its evidence against the guide's threshold, and whether
/// it is kept is settled by the person.
/// </para>
/// <para>
/// Every excerpt has to be in the writing word for word. A citation is the
/// person's reason to believe a rule, and one the agent paraphrased is a
/// citation of something nobody wrote.
/// </para>
/// </remarks>
public static partial class VoiceMining
{
    /// <summary>The answer's contract.</summary>
    public const string Version = "voice-mine/1";

    /// <summary>The most of an excerpt that is stored.</summary>
    public const int ExcerptCharacters = 300;

    /// <summary>The schema for <see cref="Version"/>.</summary>
    public const string Schema = """
        {
          "title": "voice-mine/1",
          "type": "object",
          "additionalProperties": false,
          "required": ["contract", "rules"],
          "properties": {
            "contract": { "const": "voice-mine/1" },
            "rules": { "type": "array", "items": {
              "type": "object", "additionalProperties": false,
              "required": ["id", "statement", "check", "surfaces", "citations"],
              "properties": {
                "id": { "type": "string" },
                "statement": { "type": "string" },
                "check": { "anyOf": [ { "type": "null" }, {
                  "type": "object", "additionalProperties": false, "required": ["type"],
                  "properties": {
                    "type": { "type": "string" },
                    "phrases": { "type": "array", "items": { "type": "string" } },
                    "spellings": { "type": "array", "items": {
                      "type": "object", "additionalProperties": false, "required": ["avoid", "prefer"],
                      "properties": { "avoid": { "type": "string" }, "prefer": { "type": "string" } } } },
                    "limit": { "type": "integer" },
                    "pattern": { "type": "string" } } } ] },
                "surfaces": { "type": "array", "items": { "enum": ["doc", "commit", "pr"] } },
                "citations": { "type": "array", "items": {
                  "type": "object", "additionalProperties": false, "required": ["evidence", "excerpt"],
                  "properties": { "evidence": { "type": "string" }, "excerpt": { "type": "string" } } } } } } }
          }
        }
        """;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>What each check type takes, for the prompt.</summary>
    private static readonly IReadOnlyDictionary<string, string> CheckFields = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [VoiceChecker.AvoidPhrase] = "phrases",
        [VoiceChecker.PreferSpelling] = "spellings: avoid and prefer",
        [VoiceChecker.HeadingCase] = "sentence-case headings",
        [VoiceChecker.MaxSentenceWords] = "limit",
        [VoiceChecker.CommitSubject] = "limit: subject length; also no full stop, capital first",
        [VoiceChecker.Regex] = "pattern that must not match; use only when nothing else fits",
    };

    [GeneratedRegex(@"^[a-z0-9][a-z0-9\-]{0,59}$")]
    private static partial Regex RuleId();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Space();

    /// <summary>The question put to the agent for one batch of writing.</summary>
    /// <param name="guide">The guide being run.</param>
    /// <param name="batch">The writing to read.</param>
    /// <param name="known">Rules already accepted, so the agent proposes what is new or would change one.</param>
    public static string Prompt(VoiceGuide guide, IReadOnlyList<VoiceEvidence> batch, IReadOnlyList<VoiceRule> known)
    {
        ArgumentNullException.ThrowIfNull(guide);
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(known);

        var prompt = new StringBuilder();

        prompt.AppendLine("Below is writing by one person, every piece of it written by hand. Work out the rules their");
        prompt.AppendLine("writing follows: how they write, not what they write about. Answer in the " + Version + " shape.");
        prompt.AppendLine();
        prompt.AppendLine(CultureInfo.InvariantCulture, $"This is the {guide.Id} guide: {guide.Summary}");
        prompt.AppendLine("Propose only rules about that.");
        prompt.AppendLine();
        prompt.AppendLine("For each rule:");
        prompt.AppendLine("- id: short, lower case, words joined by hyphens. Reuse a known rule's id only to change that rule.");
        prompt.AppendLine("- statement: the rule in one plain sentence, as an instruction to a writer.");
        prompt.AppendLine("- check: a mechanical check when one fits exactly, otherwise null. Types:");

        if (guide.CheckTypes.Count == 0)
        {
            prompt.AppendLine("  none in this guide, so always null.");
        }

        foreach (var type in guide.CheckTypes)
        {
            prompt.AppendLine(CultureInfo.InvariantCulture, $"  {type} ({CheckFields.GetValueOrDefault(type, "see the schema")})");
        }

        prompt.AppendLine("- surfaces: where it applies, from doc, commit and pr. Empty means everywhere.");
        prompt.AppendLine("- citations: every passage that shows the rule, as the evidence id and an excerpt copied from");
        prompt.AppendLine("  that piece exactly, character for character, never reworded. Cite as many as there are.");
        prompt.AppendLine();
        prompt.AppendLine("Propose only rules the writing shows more than once. A rule from one passage is a guess.");
        prompt.AppendLine("Leave out anything about subject matter, project names or facts.");

        if (known.Count > 0)
        {
            prompt.AppendLine();
            prompt.AppendLine("Rules already accepted:");

            foreach (var rule in known)
            {
                prompt.AppendLine($"- {rule.Id}: {rule.Statement}");
            }
        }

        prompt.AppendLine();
        prompt.AppendLine("The writing:");

        foreach (var piece in batch)
        {
            prompt.AppendLine();
            prompt.AppendLine(CultureInfo.InvariantCulture, $"=== {piece.Id} ({piece.Source}) ===");
            prompt.AppendLine(piece.Text);
        }

        return prompt.ToString();
    }

    /// <summary>
    /// Reads an answer into rules carrying their citations, or says why it
    /// could not, in words the agent can act on when asked again.
    /// </summary>
    public static OperationResult<IReadOnlyList<VoiceRule>> Read(
        string? json,
        VoiceGuide guide,
        IReadOnlyList<VoiceEvidence> batch)
    {
        ArgumentNullException.ThrowIfNull(guide);
        ArgumentNullException.ThrowIfNull(batch);

        if (string.IsNullOrWhiteSpace(json))
        {
            return OperationResult<IReadOnlyList<VoiceRule>>.Fail(
                $"The agent ended its turn without a {Version} answer.");
        }

        Reply? reply;

        try
        {
            reply = JsonSerializer.Deserialize<Reply>(json, Options);
        }
        catch (JsonException ex)
        {
            return OperationResult<IReadOnlyList<VoiceRule>>.Fail($"The answer is not valid JSON: {ex.Message}");
        }

        if (reply is null || !string.Equals(reply.Contract, Version, StringComparison.Ordinal))
        {
            return OperationResult<IReadOnlyList<VoiceRule>>.Fail($"The answer does not follow {Version}.");
        }

        var pieces = batch.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
        var problems = new List<string>();
        var rules = new List<VoiceRule>();

        foreach (var proposed in reply.Rules)
        {
            var id = proposed.Id.Trim().ToLowerInvariant();

            if (!RuleId().IsMatch(id) || proposed.Statement.Trim().Length == 0)
            {
                problems.Add($"rule '{proposed.Id}' needs a lower-case hyphenated id and a statement");

                continue;
            }

            if (proposed.Check is { } check
                && !guide.CheckTypes.Contains(check.Type.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                problems.Add($"rule '{id}' uses '{check.Type}', which is not a check type; use null instead");

                continue;
            }

            var citations = new List<VoiceCitation>();

            foreach (var cited in proposed.Citations)
            {
                if (!pieces.TryGetValue(cited.Evidence.Trim(), out var piece))
                {
                    problems.Add($"rule '{id}' cites '{cited.Evidence}', which is not one of the pieces");

                    continue;
                }

                if (cited.Excerpt.Trim().Length == 0 || !Flat(piece.Text).Contains(Flat(cited.Excerpt), StringComparison.Ordinal))
                {
                    problems.Add($"rule '{id}' quotes {piece.Id} with words that are not in it exactly");

                    continue;
                }

                citations.Add(new VoiceCitation
                {
                    Source = piece.Source,
                    Reference = piece.Reference,
                    Excerpt = Excerpt(cited.Excerpt),
                    Hash = piece.Hash,
                });
            }

            rules.Add(new VoiceRule
            {
                Id = id,
                Statement = proposed.Statement.Trim(),
                Check = proposed.Check is { } typed ? Check(typed) : null,
                Surfaces = [.. proposed.Surfaces.Select(s => s.Trim().ToLowerInvariant()).Distinct()],
                Citations = citations,
            });
        }

        return problems.Count > 0
            ? OperationResult<IReadOnlyList<VoiceRule>>.Fail(
                "Some of it could not be used: " + string.Join("; ", problems.Distinct()) + ".")
            : OperationResult<IReadOnlyList<VoiceRule>>.Ok(rules);
    }

    /// <summary>
    /// Folds rules from several batches into one list: the same id's
    /// citations are pooled, and the first statement and check stand.
    /// </summary>
    public static IReadOnlyList<VoiceRule> Merge(IEnumerable<VoiceRule> rules)
    {
        var merged = new Dictionary<string, VoiceRule>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();

        foreach (var rule in rules)
        {
            if (!merged.TryGetValue(rule.Id, out var existing))
            {
                merged[rule.Id] = rule;
                order.Add(rule.Id);

                continue;
            }

            foreach (var citation in rule.Citations)
            {
                if (!existing.Citations.Any(c => Same(c, citation)))
                {
                    existing.Citations.Add(citation);
                }
            }
        }

        return order.Select(id => merged[id]).ToList();
    }

    private static bool Same(VoiceCitation a, VoiceCitation b) =>
        string.Equals(a.Reference, b.Reference, StringComparison.Ordinal)
        && string.Equals(a.Excerpt, b.Excerpt, StringComparison.Ordinal);

    private static VoiceCheck Check(CheckReply reply) => new()
    {
        Type = reply.Type.Trim().ToLowerInvariant(),
        Phrases = [.. reply.Phrases.Where(p => p.Trim().Length > 0)],
        Spellings = reply.Spellings
            .Where(s => s.Avoid.Trim().Length > 0)
            .GroupBy(s => s.Avoid.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Prefer.Trim(), StringComparer.OrdinalIgnoreCase),
        Limit = reply.Limit,
        Pattern = reply.Pattern,
    };

    private static string Flat(string text) => Space().Replace(text, " ").Trim();

    private static string Excerpt(string text)
    {
        var flat = Flat(text);

        return flat.Length <= ExcerptCharacters ? flat : flat[..ExcerptCharacters] + "…";
    }

    private sealed class Reply
    {
        public string Contract { get; set; } = string.Empty;

        public List<RuleReply> Rules { get; set; } = [];
    }

    private sealed class RuleReply
    {
        public string Id { get; set; } = string.Empty;

        public string Statement { get; set; } = string.Empty;

        public CheckReply? Check { get; set; }

        public List<string> Surfaces { get; set; } = [];

        public List<CitationReply> Citations { get; set; } = [];
    }

    private sealed class CheckReply
    {
        public string Type { get; set; } = string.Empty;

        public List<string> Phrases { get; set; } = [];

        public List<SpellingReply> Spellings { get; set; } = [];

        public int? Limit { get; set; }

        public string? Pattern { get; set; }
    }

    private sealed class SpellingReply
    {
        public string Avoid { get; set; } = string.Empty;

        public string Prefer { get; set; } = string.Empty;
    }

    private sealed class CitationReply
    {
        public string Evidence { get; set; } = string.Empty;

        public string Excerpt { get; set; } = string.Empty;
    }
}
