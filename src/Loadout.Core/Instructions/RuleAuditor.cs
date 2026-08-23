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
    public static RuleAudit Audit(
        IReadOnlyList<RuleDocument> rules,
        IReadOnlyList<string> coreInstructionPaths,
        string slug)
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
            coreLines.AddRange(InstructionLines(text));
        }

        AuditIndividualRules(findings, rules);
        AuditDuplication(findings, rules, coreLines);
        AuditGlobOverlap(findings, rules);

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
    private static IEnumerable<string> InstructionLines(string text)
    {
        var inFence = false;

        foreach (var raw in text.Split('\n'))
        {
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

            yield return line;
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
}
