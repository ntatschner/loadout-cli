using System.Globalization;
using System.Text;
using System.Text.Json;
using Loadout.Models.Results;
using Loadout.Models.Voice;

namespace Loadout.Core.Voice;

/// <summary>A piece of writing put to a model for scoring.</summary>
/// <param name="Id">Short id it is answered by, such as <c>s3</c>.</param>
/// <param name="Text">The text.</param>
public sealed record VoiceSample(string Id, string Text);

/// <summary>A sample rewritten to break one rule, so a rule the history never breaks can still be tested.</summary>
/// <param name="Rule">The rule it is meant to break.</param>
/// <param name="Sample">The held-out sample it was rewritten from.</param>
/// <param name="Text">The rewritten text.</param>
public sealed record VoiceVariant(string Rule, string Sample, string Text);

/// <summary>
/// The two model rounds scoring needs: one rewrites held-out writing to break
/// a rule, the other judges samples against rules no checker can read.
/// </summary>
/// <remarks>
/// Both are answered in a fixed shape and read strictly, like mining. An
/// answer naming a rule or a sample it was not given is refused and asked
/// again, because a verdict about something that was never shown is a guess.
/// </remarks>
public static class VoiceJudging
{
    /// <summary>The judging contract.</summary>
    public const string JudgeVersion = "voice-judge/1";

    /// <summary>The variants contract.</summary>
    public const string VariantsVersion = "voice-variants/1";

    /// <summary>The most of one sample a round reads, so one long piece cannot crowd the rest out.</summary>
    public const int SampleCharacters = 2_000;

    /// <summary>The judging answer's shape.</summary>
    public const string JudgeSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["contract", "verdicts"],
          "properties": {
            "contract": { "const": "voice-judge/1" },
            "verdicts": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["rule", "sample", "breaks"],
                "properties": {
                  "rule": { "type": "string" },
                  "sample": { "type": "string" },
                  "breaks": { "type": "boolean" }
                }
              }
            }
          }
        }
        """;

    /// <summary>The variants answer's shape.</summary>
    public const string VariantsSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["contract", "variants"],
          "properties": {
            "contract": { "const": "voice-variants/1" },
            "variants": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["rule", "sample", "text"],
                "properties": {
                  "rule": { "type": "string" },
                  "sample": { "type": "string" },
                  "text": { "type": "string" }
                }
              }
            }
          }
        }
        """;

    /// <summary>A sample cut to the most a round reads of one.</summary>
    public static string Clip(string text) =>
        text.Length <= SampleCharacters ? text : text[..SampleCharacters];

    /// <summary>The question asking for one variant of a sample for each rule.</summary>
    /// <param name="requests">Each rule with the sample to rewrite for it.</param>
    public static string VariantsPrompt(IReadOnlyList<(VoiceRule Rule, VoiceSample Sample)> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);

        var prompt = new StringBuilder();

        prompt.AppendLine("Each item below is a writing rule and a sample that follows it. Rewrite the sample so that it");
        prompt.AppendLine("breaks that rule, plainly, and changes nothing else: same meaning, same length near enough, every");
        prompt.AppendLine("other habit of the writing kept. Answer in the " + VariantsVersion + " shape, one variant per item,");
        prompt.AppendLine("naming the rule and the sample by their ids.");

        foreach (var (rule, sample) in requests)
        {
            prompt.AppendLine();
            prompt.AppendLine(CultureInfo.InvariantCulture, $"=== rule {rule.Id}: {rule.Statement} ===");
            prompt.AppendLine(CultureInfo.InvariantCulture, $"--- sample {sample.Id} ---");
            prompt.AppendLine(sample.Text);
        }

        return prompt.ToString();
    }

    /// <summary>Reads variants, refusing any for a rule or sample that was not asked about.</summary>
    public static OperationResult<IReadOnlyList<VoiceVariant>> ReadVariants(
        string? json,
        IReadOnlyList<(VoiceRule Rule, VoiceSample Sample)> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);

        if (string.IsNullOrWhiteSpace(json))
        {
            return OperationResult<IReadOnlyList<VoiceVariant>>.Fail("The answer was empty.");
        }

        var asked = requests.ToDictionary(r => (r.Rule.Id, r.Sample.Id), r => r.Sample.Text);
        var variants = new List<VoiceVariant>();

        try
        {
            using var document = JsonDocument.Parse(json);

            foreach (var item in document.RootElement.GetProperty("variants").EnumerateArray())
            {
                var rule = item.GetProperty("rule").GetString() ?? string.Empty;
                var sample = item.GetProperty("sample").GetString() ?? string.Empty;
                var text = item.GetProperty("text").GetString() ?? string.Empty;

                if (!asked.TryGetValue((rule, sample), out var original))
                {
                    return OperationResult<IReadOnlyList<VoiceVariant>>.Fail(
                        $"A variant names rule '{rule}' and sample '{sample}', which were not asked about together.");
                }

                // A variant that changed nothing tests nothing, and would
                // count against a rule for not firing on writing that follows it.
                if (text.Trim().Length == 0 || string.Equals(text.Trim(), original.Trim(), StringComparison.Ordinal))
                {
                    continue;
                }

                variants.Add(new VoiceVariant(rule, sample, Clip(text)));
            }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return OperationResult<IReadOnlyList<VoiceVariant>>.Fail($"The answer was not in the {VariantsVersion} shape: {e.Message}");
        }

        return OperationResult<IReadOnlyList<VoiceVariant>>.Ok(variants);
    }

    /// <summary>The question asking whether each sample breaks each rule.</summary>
    public static string JudgePrompt(IReadOnlyList<VoiceRule> rules, IReadOnlyList<VoiceSample> samples)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(samples);

        var prompt = new StringBuilder();

        prompt.AppendLine("Below are writing rules and samples of writing. For every rule and every sample, say whether");
        prompt.AppendLine("the sample breaks the rule. A sample that gives a rule nothing to go on does not break it.");
        prompt.AppendLine("Judge each pair on its own. Answer in the " + JudgeVersion + " shape, with a verdict for every");
        prompt.AppendLine("rule and sample, naming them by their ids.");
        prompt.AppendLine();
        prompt.AppendLine("Rules:");

        foreach (var rule in rules)
        {
            prompt.AppendLine(CultureInfo.InvariantCulture, $"- {rule.Id}: {rule.Statement}");
        }

        foreach (var sample in samples)
        {
            prompt.AppendLine();
            prompt.AppendLine(CultureInfo.InvariantCulture, $"=== {sample.Id} ===");
            prompt.AppendLine(sample.Text);
        }

        return prompt.ToString();
    }

    /// <summary>
    /// Reads verdicts as the set of rule and sample pairs judged broken. A pair
    /// the answer leaves out is read as not broken, the same as a sample that
    /// gives the rule nothing to go on.
    /// </summary>
    public static OperationResult<ISet<(string Rule, string Sample)>> ReadVerdicts(
        string? json,
        IReadOnlyList<VoiceRule> rules,
        IReadOnlyList<VoiceSample> samples)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(samples);

        if (string.IsNullOrWhiteSpace(json))
        {
            return OperationResult<ISet<(string, string)>>.Fail("The answer was empty.");
        }

        var ruleIds = rules.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        var sampleIds = samples.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        var broken = new HashSet<(string, string)>();

        try
        {
            using var document = JsonDocument.Parse(json);

            foreach (var item in document.RootElement.GetProperty("verdicts").EnumerateArray())
            {
                var rule = item.GetProperty("rule").GetString() ?? string.Empty;
                var sample = item.GetProperty("sample").GetString() ?? string.Empty;

                if (!ruleIds.Contains(rule) || !sampleIds.Contains(sample))
                {
                    return OperationResult<ISet<(string, string)>>.Fail(
                        $"A verdict names rule '{rule}' and sample '{sample}', and only the rules and samples given can be judged.");
                }

                if (item.GetProperty("breaks").GetBoolean())
                {
                    broken.Add((rule, sample));
                }
            }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return OperationResult<ISet<(string, string)>>.Fail($"The answer was not in the {JudgeVersion} shape: {e.Message}");
        }

        return OperationResult<ISet<(string, string)>>.Ok(broken);
    }
}
