using System.Text;
using System.Text.RegularExpressions;
using Loadout.Models;
using Loadout.Models.Instructions;
using Loadout.Models.Results;

namespace Loadout.Core.Instructions;

/// <summary>
/// Decomposes an oversized instruction file into a small always-loaded core and
/// a set of path-scoped rules.
/// <para>
/// This is the tool that creates the layer <see cref="RuleService"/> is built to
/// read. Scoping only pays once the instructions are actually split, and doing
/// that by hand on a file that has grown for a year is the kind of job people
/// start and abandon.
/// </para>
/// <para>
/// Content is moved verbatim and never reworded. The splitter's whole claim is
/// that the result says exactly what the source said, and it proves that by
/// counting: every non-blank line in the source must appear at least as often
/// across the outputs, or the split is refused rather than applied.
/// </para>
/// </summary>
public sealed partial class InstructionSplitter
{
    /// <summary>
    /// Marker written into a core file that has been split, so a second run
    /// cannot rebuild the rules from a source that no longer holds their
    /// content.
    /// </summary>
    internal const string SplitMarker = "<!-- loadout: split -->";

    private const string IndexHeading = "## Rules held separately";

    /// <summary>
    /// How many distinct rule files a core document must point at before it is
    /// taken to have been split already. One is a passing reference; several is
    /// an index.
    /// </summary>
    private const int MinimumRuleReferences = 3;

    /// <summary>
    /// Works out what the split would produce, without writing anything.
    /// </summary>
    /// <param name="sourcePath">Instruction file to split.</param>
    /// <param name="map">How to route the sections.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<OperationResult<SplitPlan>> PlanAsync(
        string sourcePath,
        SplitMap map,
        CancellationToken ct = default)
    {
        if (!File.Exists(sourcePath))
        {
            return OperationResult<SplitPlan>.Fail(
                $"'{sourcePath}' does not exist.", ExitCode.InvalidArguments);
        }

        var unscoped = map.Rules.Where(r => r.Globs.Count == 0).Select(r => r.Name).ToList();

        if (unscoped.Count > 0)
        {
            // A rule with no globs loads every session, so splitting into one
            // moves text around without reducing what anything costs.
            return OperationResult<SplitPlan>.Fail(
                $"These rules declare no globs, so they would load on every session anyway: "
                + $"{string.Join(", ", unscoped)}. Give each one the paths it applies to.",
                ExitCode.ConfigurationInvalid);
        }

        string text;

        try
        {
            text = await File.ReadAllTextAsync(sourcePath, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<SplitPlan>.Fail($"Could not read '{sourcePath}': {ex.Message}");
        }

        if (text.Contains(SplitMarker, StringComparison.Ordinal))
        {
            return OperationResult<SplitPlan>.Fail(
                $"'{Path.GetFileName(sourcePath)}' has already been split. Running again would "
                + "rebuild the rules from a file whose content has already moved out of it. Edit "
                + "the rules directly instead.",
                ExitCode.InvalidArguments);
        }

        if (LooksAlreadySplit(text))
        {
            // Detected by shape rather than by marker, because this launcher is
            // not the only thing that has ever split an instruction file. A
            // repository that arrives already organised this way is the good
            // case, not an error — but splitting it a second time would rebuild
            // its rules out of the summary that was left behind, replacing real
            // instructions with a list of their own filenames.
            return OperationResult<SplitPlan>.Fail(
                $"'{Path.GetFileName(sourcePath)}' looks as though something has already split "
                + "it: it points at rule files rather than containing the detail itself. "
                + "Splitting it again would rebuild those rules from the summary left in its "
                + "place.\n"
                + "If those rules live in the repository, 'loadout migrate' moves them into the "
                + "workspace where the launcher reads them.",
                ExitCode.InvalidArguments);
        }

        var (preamble, sections) = Parse(text);
        var routed = Route(map, sections);

        var core = BuildCore(preamble, routed.Kept, routed.Rules);
        var rules = BuildRules(map, routed.Rules);

        var missing = FindMissingLines(text, core, rules);

        return OperationResult<SplitPlan>.Ok(new SplitPlan(sourcePath, core, rules, missing));
    }

    /// <summary>
    /// Carries out a plan. Refuses a plan that does not account for every line.
    /// </summary>
    /// <param name="plan">Plan produced by <see cref="PlanAsync"/>.</param>
    /// <param name="ruleDirectory">Where the rule files are written.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<OperationResult<SplitPlan>> ApplyAsync(
        SplitPlan plan,
        string ruleDirectory,
        CancellationToken ct = default)
    {
        if (!plan.IsLossless)
        {
            return OperationResult<SplitPlan>.Fail(
                $"{plan.MissingLines.Count} line(s) from '{Path.GetFileName(plan.SourcePath)}' are "
                + "not accounted for in the output, so the split was not applied. Widen the map "
                + "until every line has somewhere to go.",
                ExitCode.PolicyViolation);
        }

        try
        {
            Directory.CreateDirectory(ruleDirectory);

            foreach (var rule in plan.Rules)
            {
                ct.ThrowIfCancellationRequested();

                await File.WriteAllTextAsync(
                    Path.Combine(ruleDirectory, rule.Name + ".md"),
                    RenderRule(rule),
                    ct).ConfigureAwait(false);
            }

            await File.WriteAllTextAsync(plan.SourcePath, plan.Core, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<SplitPlan>.Fail($"The split could not be written: {ex.Message}");
        }

        return OperationResult<SplitPlan>.Ok(plan with { Applied = true });
    }

    /// <summary>
    /// Whether an instruction file has already been decomposed, by this
    /// launcher or by anything else.
    /// <para>
    /// The test is what the file does rather than what wrote it: a core file
    /// that lists several rule files is one whose detail has already moved out,
    /// whatever produced it. A single mention is not enough, because a file may
    /// reasonably reference one rule in passing.
    /// </para>
    /// </summary>
    internal static bool LooksAlreadySplit(string text)
    {
        if (SplitHeading().IsMatch(text))
        {
            return true;
        }

        return RuleReference().Matches(text)
            .Select(match => match.Groups["name"].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() >= MinimumRuleReferences;
    }

    /// <summary>Paths a split would write, so they can be captured in a backup first.</summary>
    public static IReadOnlyList<string> AffectedPaths(SplitPlan plan, string ruleDirectory) =>
        plan.Rules
            .Select(r => Path.Combine(ruleDirectory, r.Name + ".md"))
            .Prepend(plan.SourcePath)
            .ToList();

    /// <summary>
    /// Builds a starter map listing every section in the file, so somebody can
    /// edit routing decisions rather than invent the document from nothing.
    /// </summary>
    public async Task<OperationResult<SplitMap>> SuggestMapAsync(
        string sourcePath,
        CancellationToken ct = default)
    {
        if (!File.Exists(sourcePath))
        {
            return OperationResult<SplitMap>.Fail(
                $"'{sourcePath}' does not exist.", ExitCode.InvalidArguments);
        }

        string text;

        try
        {
            text = await File.ReadAllTextAsync(sourcePath, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<SplitMap>.Fail($"Could not read '{sourcePath}': {ex.Message}");
        }

        var (_, sections) = Parse(text);
        var map = new SplitMap();

        foreach (var section in sections)
        {
            // Suggested, never assumed. Every section starts routed to a rule
            // named after itself with no globs, which the splitter refuses to
            // apply: the person has to say what each one is for, and that is
            // the decision the tool cannot make.
            var name = Slug(section.Title);

            if (name.Length == 0 || map.Rules.Any(r => r.Name == name))
            {
                continue;
            }

            map.Rules.Add(new RuleTarget { Name = name, Description = section.Title });
            map.Sections.Add(new SectionRoute { Pattern = section.Title, Rule = name });
        }

        return OperationResult<SplitMap>.Ok(map);
    }

    private sealed record Section(string Title, string Heading, List<string> Lines);

    /// <summary>
    /// Cuts the document at level-two headings. Everything above the first one
    /// is the preamble and always stays: it is the part that says what the file
    /// is.
    /// </summary>
    private static (List<string> Preamble, List<Section> Sections) Parse(string text)
    {
        var preamble = new List<string>();
        var sections = new List<Section>();
        Section? current = null;

        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            var heading = Heading().Match(line);

            if (heading.Success)
            {
                current = new Section(heading.Groups["title"].Value.Trim(), line, []);
                sections.Add(current);

                continue;
            }

            if (current is null)
            {
                preamble.Add(line);
            }
            else
            {
                current.Lines.Add(line);
            }
        }

        return (preamble, sections);
    }

    private sealed record Routed(List<Section> Kept, Dictionary<string, List<string>> Rules);

    private static Routed Route(SplitMap map, List<Section> sections)
    {
        var kept = new List<Section>();
        var rules = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var section in sections)
        {
            var route = map.Sections.FirstOrDefault(r => Matches(r.Pattern, section.Title));

            if (route is not null)
            {
                var target = Lines(rules, route.Rule);
                target.Add(section.Heading);
                target.AddRange(section.Lines);

                continue;
            }

            var bulletRoutes = map.Bullets
                .Where(b => Matches(b.Section, section.Title))
                .ToList();

            if (bulletRoutes.Count == 0)
            {
                kept.Add(section);
                continue;
            }

            kept.Add(SplitBullets(section, bulletRoutes, rules));
        }

        return new Routed(kept, rules);
    }

    /// <summary>
    /// Moves individual bullets out of a section that otherwise stays.
    /// <para>
    /// A bullet is its own line plus every continuation line under it, so a
    /// multi-line bullet moves in one piece rather than leaving its tail behind
    /// in the core.
    /// </para>
    /// </summary>
    private static Section SplitBullets(
        Section section,
        List<BulletRoute> routes,
        Dictionary<string, List<string>> rules)
    {
        var kept = new List<string>();
        List<string>? destination = null;

        foreach (var line in section.Lines)
        {
            if (Bullet().IsMatch(line))
            {
                var route = routes.FirstOrDefault(
                    r => line.Contains(r.Contains, StringComparison.OrdinalIgnoreCase));

                if (route is null)
                {
                    destination = null;
                    kept.Add(line);

                    continue;
                }

                destination = Lines(rules, route.Rule);

                if (destination.Count == 0)
                {
                    destination.Add(section.Heading);
                }

                destination.Add(line);

                continue;
            }

            // Not a bullet: it belongs to whichever bullet preceded it, unless
            // that bullet stayed.
            (destination ?? kept).Add(line);
        }

        return section with { Lines = kept };
    }

    private static List<string> Lines(Dictionary<string, List<string>> rules, string name)
    {
        if (!rules.TryGetValue(name, out var lines))
        {
            lines = [];
            rules[name] = lines;
        }

        return lines;
    }

    private static string BuildCore(
        List<string> preamble,
        List<Section> kept,
        Dictionary<string, List<string>> rules)
    {
        var builder = new StringBuilder();

        foreach (var line in preamble)
        {
            builder.AppendLine(line);
        }

        foreach (var section in kept)
        {
            builder.AppendLine(section.Heading);

            foreach (var line in section.Lines)
            {
                builder.AppendLine(line);
            }
        }

        if (rules.Count == 0)
        {
            return builder.ToString().TrimEnd() + "\n";
        }

        // The index is what stops the split from hiding things. Without it the
        // instructions simply appear to have been deleted.
        builder.AppendLine();
        builder.AppendLine(IndexHeading);
        builder.AppendLine();
        builder.AppendLine(
            "These are not loaded every session. Read one when the work touches the paths it "
            + "covers.");
        builder.AppendLine();

        foreach (var name in rules.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine($"- `{name}`");
        }

        builder.AppendLine();
        builder.AppendLine(SplitMarker);

        return builder.ToString().TrimEnd() + "\n";
    }

    private static List<SplitRule> BuildRules(
        SplitMap map,
        Dictionary<string, List<string>> routed)
    {
        var rules = new List<SplitRule>();

        foreach (var (name, lines) in routed.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            var target = map.Rules.FirstOrDefault(
                r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            var headings = lines
                .Select(l => Heading().Match(l))
                .Where(m => m.Success)
                .Select(m => m.Groups["title"].Value.Trim())
                .ToList();

            rules.Add(new SplitRule(
                name,
                target?.Description ?? name,
                target?.Globs ?? [],
                string.Join("\n", lines).Trim() + "\n",
                headings));
        }

        return rules;
    }

    private static string RenderRule(SplitRule rule) =>
        new StringBuilder()
            .AppendLine("---")
            .AppendLine($"description: {rule.Description}")
            .AppendLine($"globs: {string.Join(", ", rule.Globs)}")
            .AppendLine("alwaysApply: false")
            .AppendLine("---")
            .AppendLine()
            .AppendLine("<!-- Moved out of the core instructions. The content is verbatim. -->")
            .AppendLine()
            .AppendLine(rule.Body.TrimEnd())
            .ToString();

    /// <summary>
    /// Counts every non-blank line in the source and in the outputs, and reports
    /// any that the outputs hold fewer times.
    /// <para>
    /// A multiset rather than a set, so a line that legitimately appears twice
    /// in the source and once in the output is still caught. This check is the
    /// reason the split can be trusted on a file nobody wants to lose.
    /// </para>
    /// </summary>
    private static List<string> FindMissingLines(
        string source,
        string core,
        IReadOnlyList<SplitRule> rules)
    {
        var produced = Count(core);

        foreach (var rule in rules)
        {
            foreach (var (line, count) in Count(rule.Body))
            {
                produced[line] = produced.GetValueOrDefault(line) + count;
            }
        }

        var missing = new List<string>();

        foreach (var (line, count) in Count(source))
        {
            // The index the splitter writes is new content, not moved content,
            // so its own lines are exempt from the accounting.
            if (produced.GetValueOrDefault(line) < count)
            {
                missing.Add(line);
            }
        }

        return missing;
    }

    private static Dictionary<string, int> Count(string text)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();

            if (line.Length == 0)
            {
                continue;
            }

            counts[line] = counts.GetValueOrDefault(line) + 1;
        }

        return counts;
    }

    /// <summary>
    /// Matches a heading against a map pattern, with <c>*</c> as a wildcard so a
    /// map does not break the first time somebody rewords a heading.
    /// </summary>
    internal static bool Matches(string pattern, string title)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        var regex = "^" + string.Join(
            ".*",
            pattern.Split('*').Select(Regex.Escape)) + "$";

        try
        {
            return Regex.IsMatch(title, regex, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250));
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static string Slug(string title)
    {
        var cleaned = new string(title
            .Trim()
            .Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-')
            .ToArray());

        while (cleaned.Contains("--", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("--", "-", StringComparison.Ordinal);
        }

        return cleaned.Trim('-');
    }

    [GeneratedRegex(@"^##\s+(?<title>.+?)\s*$", RegexOptions.None, 1000)]
    private static partial Regex Heading();

    /// <summary>
    /// Headings that announce an index of rules. Covers the wording this
    /// splitter writes and the one used by the toolkit these projects were
    /// organised with before the launcher existed.
    /// </summary>
    [GeneratedRegex(@"(?im)^##\s+.*\b(subsystem notes|rules held separately|path-scoped)\b",
        RegexOptions.None, 1000)]
    private static partial Regex SplitHeading();

    [GeneratedRegex(@"(?i)rules/(?<name>[A-Za-z0-9._-]+)\.md", RegexOptions.None, 1000)]
    private static partial Regex RuleReference();

    [GeneratedRegex(@"^\s*[-*]\s+\S", RegexOptions.None, 1000)]
    private static partial Regex Bullet();
}
