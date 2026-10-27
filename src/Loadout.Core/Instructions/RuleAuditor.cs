using System.Text.RegularExpressions;
using Loadout.Models.Instructions;

namespace Loadout.Core.Instructions;

/// <summary>
/// Checks the instruction layer for the defects that cost a session tokens
/// without anybody noticing.
/// <para>
/// Separate from <see cref="RuleService"/> deliberately: loading rules happens
/// on every launch and must stay cheap, whereas auditing them reads the core
/// instruction files as well and is something a person asks for.
/// </para>
/// </summary>
public static partial class RuleAuditor
{
    /// <summary>
    /// A rule past this size is not really a rule any more. Splitting it lets
    /// the parts be scoped separately, which is the only way any of it becomes
    /// optional.
    /// </summary>
    private const long MaximumRuleBytes = 8 * 1024;

    /// <summary>
    /// The point past which an always-loaded instruction file is crowding out
    /// the work it is meant to guide.
    /// </summary>
    private const long MaximumCoreBytes = 20 * 1024;

    /// <summary>
    /// Instruction lines shorter than this are not compared. Short lines repeat
    /// innocently, and reporting them would bury the duplication that matters.
    /// </summary>
    private const int MinimumComparableLength = 30;

    /// <summary>
    /// Audits the rules against the core instruction files that load alongside
    /// them.
    /// </summary>
    /// <param name="rules">Rules as loaded for the project.</param>
    /// <param name="coreInstructionPaths">
    /// Absolute paths to the always-loaded instruction files, in the order the
    /// compiler would read them.
    /// </param>
    /// <param name="slug">Project being audited.</param>
    /// <param name="memoryPaths">
    /// Absolute paths to the memory topics a session can be pointed at, read
    /// only for wording that invites narration. None when omitted.
    /// </param>
    public static RuleAudit Audit(
        IReadOnlyList<RuleDocument> rules,
        IReadOnlyList<string> coreInstructionPaths,
        string slug,
        IReadOnlyList<string>? memoryPaths = null)
    {
        var findings = new List<RuleFinding>();
        var coreBytes = 0L;
        var coreLines = new List<string>();

        foreach (var path in coreInstructionPaths)
        {
            if (!File.Exists(path))
            {
                findings.Add(new RuleFinding(null, RuleFindingSeverity.Error, "core-missing",
                    $"The manifest lists '{Path.GetFileName(path)}' as project context, but it does "
                    + "not exist, so every launch quietly loads less than it says it does."));

                continue;
            }

            var info = new FileInfo(path);
            coreBytes += info.Length;

            string text;

            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                findings.Add(new RuleFinding(null, RuleFindingSeverity.Error, "core-unreadable",
                    $"'{Path.GetFileName(path)}' could not be read: {ex.Message}"));

                continue;
            }

            if (info.Length > MaximumCoreBytes)
            {
                findings.Add(new RuleFinding(null, RuleFindingSeverity.Warning, "core-oversized",
                    $"'{Path.GetFileName(path)}' is {info.Length / 1024}KB and loads on every "
                    + "session. Split the parts that only matter sometimes into scoped rules."));
            }

            AuditImports(findings, path, text);
            AuditNarration(findings, null, Path.GetFileName(path), text);
            coreLines.AddRange(InstructionLines(text));
        }

        AuditIndividualRules(findings, rules);
        AuditDuplication(findings, rules, coreLines);
        AuditGlobOverlap(findings, rules);

        foreach (var rule in rules)
        {
            AuditNarration(findings, rule.Name, Path.GetFileName(rule.Path), ReadOr(rule.Path, rule.Body));
        }

        foreach (var path in memoryPaths ?? [])
        {
            AuditNarration(findings, null, $"memory/{Path.GetFileName(path)}", ReadOr(path, null));
        }

        var budget = new InstructionBudget(
            coreBytes,
            rules.Where(r => r.AlwaysApply).ToList(),
            rules.Where(r => !r.AlwaysApply && r.Globs.Count > 0).ToList(),
            rules.Where(r => r.IsUnscoped).ToList());

        return new RuleAudit(slug, rules, findings, budget);
    }

    /// <summary>
    /// Follows <c>@import</c> lines.
    /// <para>
    /// An import pulls another file into every session, and its cost does not
    /// appear anywhere in the importing file's own size. That is the most common
    /// way an instruction budget turns out to be several times what somebody
    /// thought it was.
    /// </para>
    /// </summary>
    private static void AuditImports(List<RuleFinding> findings, string path, string text)
    {
        var directory = Path.GetDirectoryName(path) ?? ".";

        foreach (Match match in Import().Matches(text))
        {
            var target = match.Groups["path"].Value.Trim();
            var resolved = Path.GetFullPath(Path.Combine(directory, target));

            if (!File.Exists(resolved))
            {
                findings.Add(new RuleFinding(null, RuleFindingSeverity.Error, "import-missing",
                    $"'{Path.GetFileName(path)}' imports '{target}', which does not exist."));

                continue;
            }

            findings.Add(new RuleFinding(null, RuleFindingSeverity.Info, "import",
                $"'{Path.GetFileName(path)}' imports '{target}', adding "
                + $"{new FileInfo(resolved).Length / 1024}KB to every session."));
        }
    }

    private static void AuditIndividualRules(
        List<RuleFinding> findings,
        IReadOnlyList<RuleDocument> rules)
    {
        foreach (var rule in rules)
        {
            if (rule.Bytes > MaximumRuleBytes)
            {
                findings.Add(new RuleFinding(rule.Name, RuleFindingSeverity.Warning, "oversized",
                    $"is {rule.Bytes / 1024}KB. Split it so the parts can be scoped separately."));
            }

            if (string.IsNullOrWhiteSpace(rule.Description))
            {
                findings.Add(new RuleFinding(rule.Name, RuleFindingSeverity.Info, "no-description",
                    "has no description, so a listing cannot say when to read it."));
            }

            if (rule.IsUnscoped)
            {
                findings.Add(new RuleFinding(rule.Name, RuleFindingSeverity.Warning, "unscoped",
                    "declares neither globs nor alwaysApply, so it loads every session by "
                    + "default. Give it a scope, or set alwaysApply: true to say that is meant."));
            }

            // The contradiction that hides in plain sight: the listing shows
            // globs, the author believes the rule is scoped, and it loads every
            // session regardless because alwaysApply outranks them.
            if (rule.AlwaysApply && rule.Globs.Count > 0 && !rule.Globs.All(IsUniversal))
            {
                findings.Add(new RuleFinding(rule.Name, RuleFindingSeverity.Warning, "always-with-globs",
                    $"sets alwaysApply: true and also declares globs ({string.Join(", ", rule.Globs)}). "
                    + "The globs are decorative: it loads every session. Drop one or the other."));
            }
        }
    }

    /// <summary>
    /// Finds wording that asks the agent to explain itself rather than simply
    /// do what is written.
    /// </summary>
    /// <remarks>
    /// <para>
    /// "This overrides the session default" is true and is also an invitation:
    /// an agent told that one instruction beats another tends to say, every
    /// time, which one it chose and why. The same goes for "confirm that you"
    /// and "tell me when you". A plain directive, such as "Do not add
    /// Co-Authored-By trailers", is followed without comment.
    /// </para>
    /// <para>
    /// A short list of phrasings, not a judgement of every sentence, so it
    /// misses paraphrases. That is the price of a check that runs on every
    /// doctor without asking a model anything. A warning, because the wording
    /// costs attention rather than correctness.
    /// </para>
    /// </remarks>
    private static void AuditNarration(
        List<RuleFinding> findings,
        string? rule,
        string file,
        string? text)
    {
        if (text is null)
        {
            return;
        }

        foreach (var (number, line) in NumberedInstructionLines(text))
        {
            if (InvitesNarration().IsMatch(line))
            {
                findings.Add(new RuleFinding(rule, RuleFindingSeverity.Warning, "invites-narration",
                    $"'{file}' line {number} invites the agent to say it is following it: "
                    + $"\"{Truncate(line)}\". Write it as a plain directive, such as \"Do not "
                    + "add Co-Authored-By trailers\", and it is followed without comment."));
            }
        }
    }

    private static string? ReadOr(string path, string? fallback)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : fallback;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return fallback;
        }
    }

    private static bool IsUniversal(string glob) =>
        glob.Trim() is "**" or "**/*" or "*";

    private static void AuditDuplication(
        List<RuleFinding> findings,
        IReadOnlyList<RuleDocument> rules,
        IReadOnlyList<string> coreLines)
    {
        var core = coreLines
            .Select(Normalise)
            .Where(line => line.Length >= MinimumComparableLength)
            .ToHashSet(StringComparer.Ordinal);

        var seen = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var rule in rules)
        {
            foreach (var line in InstructionLines(rule.Body))
            {
                var key = Normalise(line);

                if (key.Length < MinimumComparableLength)
                {
                    continue;
                }

                if (core.Contains(key))
                {
                    findings.Add(new RuleFinding(rule.Name, RuleFindingSeverity.Warning, "duplicates-core",
                        $"repeats an instruction already in the core context: \"{Truncate(line)}\". "
                        + "It is paid for twice and reads as emphasis that was not intended."));

                    continue;
                }

                if (seen.TryGetValue(key, out var other))
                {
                    findings.Add(new RuleFinding(rule.Name, RuleFindingSeverity.Warning, "duplicate",
                        $"repeats an instruction from '{other}': \"{Truncate(line)}\"."));
                }
                else
                {
                    seen[key] = rule.Name;
                }
            }
        }
    }

    /// <summary>
    /// Two rules claiming the same paths both load together, so between them
    /// they are an always-apply rule that nobody declared as one.
    /// </summary>
    private static void AuditGlobOverlap(
        List<RuleFinding> findings,
        IReadOnlyList<RuleDocument> rules)
    {
        var scoped = rules.Where(r => !r.AlwaysApply && r.Globs.Count > 0).ToList();

        for (var i = 0; i < scoped.Count; i++)
        {
            for (var j = i + 1; j < scoped.Count; j++)
            {
                var shared = scoped[i].Globs
                    .Intersect(scoped[j].Globs, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (shared.Count > 0)
                {
                    findings.Add(new RuleFinding(scoped[j].Name, RuleFindingSeverity.Info, "overlapping-globs",
                        $"claims {string.Join(", ", shared)}, which '{scoped[i].Name}' also claims. "
                        + "Both load together; merge them if that was not intended."));
                }
            }
        }
    }

    /// <summary>
    /// The instruction-bearing lines of a document: bullets and prose, with
    /// headings, code fences and blank lines left out.
    /// </summary>
    private static IEnumerable<string> InstructionLines(string text) =>
        NumberedInstructionLines(text).Select(numbered => numbered.Line);

    /// <summary>
    /// <see cref="InstructionLines"/> with each line's number in the file, so
    /// a finding can say where to look.
    /// </summary>
    private static IEnumerable<(int Number, string Line)> NumberedInstructionLines(string text)
    {
        var inFence = false;
        var number = 0;

        foreach (var raw in text.Split('\n'))
        {
            number++;

            var line = raw.Trim();

            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }

            if (inFence
                || line.Length == 0
                || line.StartsWith('#')
                || line.StartsWith("<!--", StringComparison.Ordinal))
            {
                continue;
            }

            yield return (number, line);
        }
    }

    private static string Normalise(string line) =>
        NonComparable().Replace(line.TrimStart('-', '*', ' ').ToLowerInvariant(), " ").Trim();

    private static string Truncate(string value) =>
        value.Length <= 70 ? value : value[..70] + "...";

    /// <summary>
    /// Matches both spellings in circulation: a bare <c>@path</c> line and the
    /// explicit <c>@import path</c> form.
    /// </summary>
    [GeneratedRegex(@"(?m)^[ \t]*@(?:import[ \t]+)?(?<path>[^\s#]+\.md)[ \t]*$",
        RegexOptions.None, 1000)]
    private static partial Regex Import();

    [GeneratedRegex(@"[^a-z0-9 ]+", RegexOptions.None, 1000)]
    private static partial Regex NonComparable();

    /// <summary>
    /// The phrasings that ask an agent to explain its compliance: one rule
    /// said to override another, or a request to be told that it was followed.
    /// </summary>
    [GeneratedRegex(
        @"\b(?:this|it|which) overrides\b"
        + @"|\boverrides? (?:the|any|your) (?:session|system|default)"
        + @"|\bignore (?:the|any) (?:session |system )?default"
        + @"|\binstead of the (?:session|system) default"
        + @"|\b(?:tell|show) me (?:when|that) you\b"
        + @"|\bconfirm (?:that|when) you\b",
        RegexOptions.IgnoreCase, 1000)]
    private static partial Regex InvitesNarration();
}
