using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Loadout.Models.Results;
using Loadout.Models.Voice;

namespace Loadout.Core.Voice;

/// <summary>
/// The meta step: a model drafts a guide definition once, the person reviews
/// it, and it is saved under a version that never changes.
/// </summary>
/// <remarks>
/// <para>
/// A guide is drafted, not designed afresh by every run, because what a run
/// produces has to be something other features can rely on. The draft is
/// read as strictly as a mined rule. A check type the checker cannot run is
/// refused, because a rule carrying it could never be checked, and a
/// threshold outside its sensible range is refused rather than quietly
/// clamped, so the person reviews what was actually proposed.
/// </para>
/// <para>
/// The id and version are Loadout's, never the model's. The id is what the
/// person asked for, and the version is the next one free, so a draft can
/// never replace a definition somebody's profile was learned against.
/// </para>
/// <para>
/// Sources are allowed beyond the ones the engine can read today. A guide for
/// how somebody structures code needs the code, which nothing gathers yet.
/// The definition records what it would need, and the commands say which of
/// those Loadout cannot read, rather than pretending the guide is ready.
/// </para>
/// </remarks>
public static partial class VoiceGuideDrafting
{
    /// <summary>The drafting contract.</summary>
    public const string Version = "voice-guide/1";

    /// <summary>The source kinds the engine can gather today, with what each is.</summary>
    public static readonly IReadOnlyDictionary<string, string> KnownSources = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["squash"] = "squash-merge commit messages in the repository",
        ["mark"] = "files, commits and pull requests the person marked as their own writing",
        ["edit-pair"] = "pull request descriptions against the squash message they were merged under",
        ["interview"] = "the person's answers when asked about a rule directly",
        ["corpus"] = "a directory of somebody's writing, for a named style",
    };

    /// <summary>What each check type does, for the prompt.</summary>
    private static readonly IReadOnlyDictionary<string, string> CheckMeanings = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [VoiceChecker.AvoidPhrase] = "flags words or phrases that should not appear",
        [VoiceChecker.PreferSpelling] = "flags a disfavoured spelling and names the preferred one",
        [VoiceChecker.HeadingCase] = "flags headings not in sentence case",
        [VoiceChecker.MaxSentenceWords] = "flags sentences over a word limit",
        [VoiceChecker.CommitSubject] = "checks a commit subject's form",
        [VoiceChecker.Regex] = "flags text matching a pattern, and never gates",
    };

    [GeneratedRegex("^[a-z][a-z0-9-]{0,39}$")]
    private static partial Regex Slug();

    /// <summary>The answer's shape.</summary>
    public const string Schema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["contract", "summary", "sources", "surfaces", "checkTypes", "evidence", "promotion", "cap", "scoring"],
          "properties": {
            "contract": { "const": "voice-guide/1" },
            "summary": { "type": "string" },
            "sources": { "type": "array", "items": { "type": "string" } },
            "surfaces": { "type": "array", "items": { "type": "string" } },
            "checkTypes": { "type": "array", "items": { "type": "string" } },
            "evidence": {
              "type": "object",
              "additionalProperties": false,
              "required": ["citations", "sources"],
              "properties": {
                "citations": { "type": "integer" },
                "sources": { "type": "integer" }
              }
            },
            "promotion": {
              "type": "object",
              "additionalProperties": false,
              "required": ["agreement", "consecutiveRuns", "editFloor", "demotionFailures", "unconfirmedLapses"],
              "properties": {
                "agreement": { "type": "number" },
                "consecutiveRuns": { "type": "integer" },
                "editFloor": { "type": "integer" },
                "demotionFailures": { "type": "integer" },
                "unconfirmedLapses": { "type": "integer" }
              }
            },
            "cap": {
              "type": "object",
              "additionalProperties": false,
              "required": ["margin", "lowerBelow", "seedCallUsd", "batchCharacters"],
              "properties": {
                "margin": { "type": "number" },
                "lowerBelow": { "type": "number" },
                "seedCallUsd": { "type": "number" },
                "batchCharacters": { "type": "integer" }
              }
            },
            "scoring": {
              "type": "object",
              "additionalProperties": false,
              "required": ["holdOut"],
              "properties": {
                "holdOut": { "type": "number" }
              }
            }
          }
        }
        """;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    /// <summary>Why an id cannot be drafted, or null when it can.</summary>
    public static string? Refusal(string id)
    {
        if (!Slug().IsMatch(id))
        {
            return $"'{id}' is not a guide id. Use lower case letters, digits and hyphens, starting with a letter.";
        }

        return VoiceGuides.Find(id) is not null
            ? $"'{id}' ships with Loadout, so its next version comes with a release rather than a draft."
            : null;
    }

    /// <summary>The question asking for a guide definition.</summary>
    /// <param name="id">The guide's id.</param>
    /// <param name="about">What the person said the guide should learn.</param>
    public static string Prompt(string id, string about)
    {
        ArgumentNullException.ThrowIfNull(about);

        var prompt = new StringBuilder();

        prompt.AppendLine(CultureInfo.InvariantCulture, $"Draft the definition of a guide called '{id}'. A guide learns one part of how a person");
        prompt.AppendLine("writes or works from their own material, as rules they accept one at a time. The definition fixes");
        prompt.AppendLine("where evidence comes from, where the rules apply, which mechanical checks a rule may carry, and every");
        prompt.AppendLine("threshold a run is judged by. It is saved once and never changed, so choose values that hold up.");
        prompt.AppendLine();
        prompt.AppendLine("What the person wants this guide to learn:");
        prompt.AppendLine(about.Trim());
        prompt.AppendLine();
        prompt.AppendLine("Sources. Use these where they fit. You may name another source kind the guide genuinely needs,");
        prompt.AppendLine("as a short lower-case word, and it will be recorded as not yet readable:");

        foreach (var (source, meaning) in KnownSources)
        {
            prompt.AppendLine(CultureInfo.InvariantCulture, $"- {source}: {meaning}");
        }

        prompt.AppendLine();
        prompt.AppendLine("Check types. A rule may carry only one of these; anything else is judged by a model and never");
        prompt.AppendLine("gates. List only the ones that suit this guide, or none:");

        foreach (var type in VoiceChecker.Types)
        {
            prompt.AppendLine(CultureInfo.InvariantCulture, $"- {type}: {CheckMeanings.GetValueOrDefault(type, string.Empty)}");
        }

        prompt.AppendLine();
        prompt.AppendLine("Surfaces are where the rules apply, as short lower-case words, such as doc, commit and pr.");
        prompt.AppendLine("Lower the evidence threshold for a guide whose evidence comes from few places. Keep promotion");
        prompt.AppendLine("strict: a rule that gates has to be right. Here is the prose guide that ships, as an example of");
        prompt.AppendLine("sensible values, not as something to copy:");
        prompt.AppendLine();
        prompt.AppendLine(JsonSerializer.Serialize(Answer(VoiceGuides.Prose()), Options));
        prompt.AppendLine();
        prompt.AppendLine(CultureInfo.InvariantCulture, $"Answer in the {Version} shape. Leave out the id and version; they are set for you.");

        return prompt.ToString();
    }

    /// <summary>
    /// Reads a draft, refusing anything the engine could not honour or that is
    /// out of its sensible range.
    /// </summary>
    /// <param name="json">The answer.</param>
    /// <param name="id">The guide's id.</param>
    /// <param name="version">The version it will be saved as.</param>
    public static OperationResult<VoiceGuide> Read(string? json, string id, int version)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return OperationResult<VoiceGuide>.Fail("The answer was empty.");
        }

        VoiceGuide? guide;

        try
        {
            using var document = JsonDocument.Parse(json);

            if (document.RootElement.GetProperty("contract").GetString() != Version)
            {
                return OperationResult<VoiceGuide>.Fail($"The answer was not in the {Version} shape.");
            }

            guide = document.RootElement.Deserialize<VoiceGuide>(Options);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return OperationResult<VoiceGuide>.Fail($"The answer was not in the {Version} shape: {e.Message}");
        }

        if (guide is null)
        {
            return OperationResult<VoiceGuide>.Fail($"The answer was not in the {Version} shape.");
        }

        guide.Id = id;
        guide.Version = version;
        guide.Summary = guide.Summary.Trim();
        guide.Sources = [.. guide.Sources.Select(s => s.Trim()).Distinct(StringComparer.Ordinal)];
        guide.Surfaces = [.. guide.Surfaces.Select(s => s.Trim()).Distinct(StringComparer.Ordinal)];
        guide.CheckTypes = [.. guide.CheckTypes.Select(s => s.Trim()).Distinct(StringComparer.Ordinal)];

        return Problem(guide) is { } problem
            ? OperationResult<VoiceGuide>.Fail(problem)
            : OperationResult<VoiceGuide>.Ok(guide);
    }

    /// <summary>The sources a guide names that the engine cannot gather yet.</summary>
    public static IReadOnlyList<string> Unreadable(VoiceGuide guide)
    {
        ArgumentNullException.ThrowIfNull(guide);

        return guide.Sources.Where(s => !KnownSources.ContainsKey(s)).ToList();
    }

    /// <summary>What is wrong with a draft, or null when nothing is.</summary>
    internal static string? Problem(VoiceGuide guide)
    {
        if (guide.Summary.Length is 0 or > 200)
        {
            return "A guide needs a summary of a sentence, no longer than 200 characters.";
        }

        if (guide.Sources.Count == 0)
        {
            return "A guide needs at least one source to learn from.";
        }

        if (guide.Sources.FirstOrDefault(s => !Slug().IsMatch(s)) is { } badSource)
        {
            return $"'{badSource}' is not a source kind. Use a short lower-case word.";
        }

        if (guide.Surfaces.Count == 0)
        {
            return "A guide needs at least one surface for its rules to apply to.";
        }

        if (guide.Surfaces.FirstOrDefault(s => !Slug().IsMatch(s)) is { } badSurface)
        {
            return $"'{badSurface}' is not a surface. Use a short lower-case word.";
        }

        if (guide.CheckTypes.FirstOrDefault(t => !VoiceChecker.Types.Contains(t)) is { } unknown)
        {
            return $"'{unknown}' is not a check Loadout can run. It can run: {string.Join(", ", VoiceChecker.Types)}.";
        }

        var evidence = guide.Evidence;

        if (evidence.Citations < 1 || evidence.Sources < 1 || evidence.Sources > evidence.Citations)
        {
            return "The evidence threshold needs at least one citation and one source, and no more sources than citations.";
        }

        var promotion = guide.Promotion;

        if (promotion.Agreement is <= 0 or > 1
            || promotion.ConsecutiveRuns < 1
            || promotion.EditFloor < 0
            || promotion.DemotionFailures < 1
            || promotion.UnconfirmedLapses < 1)
        {
            return "Promotion needs an agreement above 0 and at most 1, and at least one run, demotion failure and unconfirmed lapse.";
        }

        var cap = guide.Cap;

        if (cap.Margin is < 0 or > 2
            || cap.LowerBelow is <= 0 or >= 1
            || cap.SeedCallUsd <= 0
            || cap.BatchCharacters < 1_000)
        {
            return "The cap needs a margin from 0 to 2, a lower-cap fraction between 0 and 1, a seed cost above zero, "
                + "and batches of at least 1,000 characters.";
        }

        if (guide.Scoring.HoldOut is < 0 or > 0.5)
        {
            return "Scoring may hold back between none and half of the writing.";
        }

        return null;
    }

    /// <summary>A guide as the answer would carry it, without the id and version.</summary>
    private static object Answer(VoiceGuide guide) => new
    {
        contract = Version,
        summary = guide.Summary,
        sources = guide.Sources,
        surfaces = guide.Surfaces,
        checkTypes = guide.CheckTypes,
        evidence = guide.Evidence,
        promotion = guide.Promotion,
        cap = guide.Cap,
        scoring = guide.Scoring,
    };
}
