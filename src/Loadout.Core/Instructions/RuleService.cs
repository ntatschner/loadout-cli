using System.Text;
using System.Text.RegularExpressions;
using Loadout.Models.Instructions;
using Loadout.Models.Results;

namespace Loadout.Core.Instructions;

/// <summary>
/// Reads path-scoped instruction rules out of the workspace
/// (spec section 11's <c>global/</c> and per-project directories).
/// <para>
/// Rules are the answer to an instruction file that has grown to the point
/// where most of it is irrelevant to most sessions. Splitting it and scoping
/// the parts means the frontend conventions cost nothing while somebody is
/// working on migrations.
/// </para>
/// </summary>
public interface IRuleService
{
    /// <summary>
    /// Loads every rule that applies to a project: the workspace-wide ones
    /// first, then the project's own, so a project rule of the same name wins.
    /// </summary>
    /// <param name="workspaceRoot">
    /// Root of the workspace to read from, passed rather than resolved so this
    /// service and its caller cannot disagree about which workspace is meant.
    /// </param>
    /// <param name="slug">Project whose rules to load.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<OperationResult<IReadOnlyList<RuleDocument>>> LoadAsync(
        string workspaceRoot,
        string slug,
        CancellationToken ct = default);

    /// <summary>
    /// Selects the rules that apply to a set of repository-relative paths.
    /// Always-apply rules are always included.
    /// </summary>
    IReadOnlyList<RuleDocument> Select(
        IReadOnlyList<RuleDocument> rules,
        IReadOnlyList<string> paths);

    /// <summary>Splits rules by how often they are paid for.</summary>
    InstructionBudget Budget(IReadOnlyList<RuleDocument> rules, long coreBytes);
}

/// <inheritdoc />
internal sealed partial class RuleService : IRuleService
{
    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<RuleDocument>>> LoadAsync(
        string workspaceRoot,
        string slug,
        CancellationToken ct = default)
    {
        var rules = new Dictionary<string, RuleDocument>(StringComparer.OrdinalIgnoreCase);

        // Workspace-wide rules first, then the project's own on top. A project
        // that needs to override the house style should be able to, and the
        // more specific location winning is the least surprising way round.
        string[] directories =
        [
            Path.Combine(workspaceRoot, "global", "rules"),
            Path.Combine(workspaceRoot, "projects", slug, "rules"),
        ];

        foreach (var directory in directories)
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*.md"))
            {
                ct.ThrowIfCancellationRequested();

                var parsed = await ParseAsync(file, ct).ConfigureAwait(false);

                if (parsed.Succeeded)
                {
                    rules[parsed.Value!.Name] = parsed.Value;
                }
            }
        }

        return OperationResult<IReadOnlyList<RuleDocument>>.Ok(
            rules.Values.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <summary>
    /// Reads one rule file. The frontmatter is optional: a plain Markdown file
    /// is a rule with no declared scope, which is reported rather than guessed
    /// at.
    /// </summary>
    internal static async Task<OperationResult<RuleDocument>> ParseAsync(
        string path,
        CancellationToken ct = default)
    {
        string text;

        try
        {
            text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<RuleDocument>.Fail($"Could not read '{path}': {ex.Message}");
        }

        var description = string.Empty;
        var globs = new List<string>();
        var alwaysApply = false;
        var body = text;

        var match = Frontmatter().Match(text);

        if (match.Success)
        {
            body = text[match.Length..];

            foreach (var line in match.Groups["front"].Value.Split('\n'))
            {
                var separator = line.IndexOf(':', StringComparison.Ordinal);

                if (separator <= 0)
                {
                    continue;
                }

                var key = line[..separator].Trim();
                var value = line[(separator + 1)..].Trim().Trim('"', '\'');

                switch (key.ToLowerInvariant())
                {
                    case "description":
                        description = value;
                        break;

                    case "globs":
                    case "glob":
                        globs.AddRange(value
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                        break;

                    case "alwaysapply":
                        alwaysApply = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                        break;
                }
            }
        }

        return OperationResult<RuleDocument>.Ok(new RuleDocument(
            Path.GetFileNameWithoutExtension(path),
            path,
            description,
            globs,
            alwaysApply,
            body.TrimStart('\r', '\n'),
            Encoding.UTF8.GetByteCount(text)));
    }

    /// <inheritdoc />
    public IReadOnlyList<RuleDocument> Select(
        IReadOnlyList<RuleDocument> rules,
        IReadOnlyList<string> paths)
    {
        var selected = new List<RuleDocument>();

        foreach (var rule in rules)
        {
            if (rule.AlwaysApply)
            {
                selected.Add(rule);
                continue;
            }

            // An unscoped rule is included. Dropping it would silently lose an
            // instruction somebody wrote deliberately; the audit is where the
            // missing scope gets reported.
            if (rule.Globs.Count == 0)
            {
                selected.Add(rule);
                continue;
            }

            if (paths.Any(path => rule.Globs.Any(glob => Matches(glob, path))))
            {
                selected.Add(rule);
            }
        }

        return selected;
    }

    /// <inheritdoc />
    public InstructionBudget Budget(IReadOnlyList<RuleDocument> rules, long coreBytes) => new(
        coreBytes,
        rules.Where(r => r.AlwaysApply).ToList(),
        rules.Where(r => !r.AlwaysApply && r.Globs.Count > 0).ToList(),
        rules.Where(r => r.IsUnscoped).ToList());

    /// <summary>
    /// Matches a path against a glob.
    /// <para>
    /// Implemented by translating to a regular expression rather than by
    /// reaching for a matching library, because the semantics have to be stable
    /// across all three platforms: separators are normalised so a rule written
    /// with forward slashes matches on Windows too.
    /// </para>
    /// </summary>
    internal static bool Matches(string glob, string path)
    {
        if (string.IsNullOrWhiteSpace(glob))
        {
            return false;
        }

        var normalisedPath = path.Replace('\\', '/').TrimStart('.', '/');
        var normalisedGlob = glob.Replace('\\', '/').Trim();

        var pattern = new StringBuilder("^");

        for (var i = 0; i < normalisedGlob.Length; i++)
        {
            var c = normalisedGlob[i];

            if (c == '*')
            {
                // A doubled star crosses directory separators; a single one
                // does not. Collapsing the two would make "**/*.cs" and
                // "*.cs" mean the same thing, which is exactly the distinction
                // a scoped rule depends on.
                if (i + 1 < normalisedGlob.Length && normalisedGlob[i + 1] == '*')
                {
                    pattern.Append(".*");
                    i++;

                    if (i + 1 < normalisedGlob.Length && normalisedGlob[i + 1] == '/')
                    {
                        i++;
                    }
                }
                else
                {
                    pattern.Append("[^/]*");
                }

                continue;
            }

            pattern.Append(c switch
            {
                '?' => "[^/]",
                _ => Regex.Escape(c.ToString()),
            });
        }

        pattern.Append('$');

        try
        {
            return Regex.IsMatch(
                normalisedPath,
                pattern.ToString(),
                RegexOptions.IgnoreCase,
                TimeSpan.FromMilliseconds(250));
        }
        catch (RegexMatchTimeoutException)
        {
            // A pathological glob must not hang a launch. Not matching is the
            // safe direction: the rule is simply not applied, and the audit
            // reports the rule rather than the session stalling.
            return false;
        }
    }

    [GeneratedRegex(@"\A---\r?\n(?<front>.*?)\r?\n---[ \t]*\r?\n",
        RegexOptions.Singleline, 1000)]
    private static partial Regex Frontmatter();
}
