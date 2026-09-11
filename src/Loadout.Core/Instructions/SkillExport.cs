using System.Text;
using Loadout.Models.Instructions;

namespace Loadout.Core.Instructions;

/// <summary>
/// Turns a skill specialist into the file an agent loads as a command.
/// </summary>
/// <remarks>
/// <para>
/// One source, two mechanisms. A skill specialist composes into the context
/// when the task points at it, and the same procedure is worth having as
/// something somebody can simply type. Writing it twice would be two files
/// saying the same thing until the day they did not, so the second is derived
/// from the first at launch and never stored.
/// </para>
/// <para>
/// The body moves verbatim. Nothing here summarises, reflows or reorders a
/// line of it — the frontmatter is replaced with the one the agent expects and
/// the guidance is passed through untouched, which is the same promise the
/// rules splitter makes and for the same reason.
/// </para>
/// </remarks>
public static class SkillExport
{
    /// <summary>
    /// The command a skill specialist is offered under, or null where it is
    /// not offered as one.
    /// </summary>
    /// <remarks>
    /// Derived from the id unless the specialist says otherwise. A few say
    /// otherwise: <c>skill.mode-switch</c> is the agent knowing how a mode
    /// works rather than a procedure anybody would start, and a command nobody
    /// would type is a command in the way of the ones they would.
    /// </remarks>
    public static string? CommandFor(SpecialistDocument specialist)
    {
        ArgumentNullException.ThrowIfNull(specialist);

        if (specialist.Kind != SpecialistKind.Skill)
        {
            return null;
        }

        var declared = specialist.Activation.Command;

        if (declared is { Length: > 0 })
        {
            return string.Equals(declared, "none", StringComparison.OrdinalIgnoreCase)
                ? null
                : declared;
        }

        var dot = specialist.Id.IndexOf('.', StringComparison.Ordinal);

        return dot >= 0 && dot < specialist.Id.Length - 1
            ? specialist.Id[(dot + 1)..]
            : specialist.Id;
    }

    /// <summary>
    /// Every skill the launcher ships that is offered as a command, rendered,
    /// keyed by the command's name.
    /// </summary>
    /// <remarks>
    /// Built-ins only. A workspace or a project supplies its own as files, and
    /// those are copied rather than rendered — somebody who wrote a
    /// <c>SKILL.md</c> meant that file, and passing it through a converter
    /// would be this deciding it knew better.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> Shipped()
    {
        var catalogue = new SpecialistLibrary()
            .LoadAsync(workspaceRoot: null).GetAwaiter().GetResult();

        var rendered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var specialist in catalogue.OfKind(SpecialistKind.Skill))
        {
            if (specialist.Origin != SpecialistOrigin.BuiltIn)
            {
                continue;
            }

            if (CommandFor(specialist) is { Length: > 0 } command)
            {
                rendered[command] = Render(specialist, command);
            }
        }

        return rendered;
    }

    /// <summary>
    /// What a session would be offered, as the command's name against the
    /// <c>SKILL.md</c> it would get: the launcher's own, then what the
    /// workspace holds for every project, then what this project holds.
    /// </summary>
    /// <remarks>
    /// One enumeration, used both to hand the skills over and to count what
    /// they cost. Two would be a launch that loads one set and a budget that
    /// reports another, which is worse than not reporting at all.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> Offered(
        string? workspacePath,
        string? slug,
        string agent)
    {
        var offered = new Dictionary<string, string>(Shipped(), StringComparer.OrdinalIgnoreCase);

        if (workspacePath is not { Length: > 0 } || slug is not { Length: > 0 })
        {
            return offered;
        }

        // Narrower last, the way everything else composes.
        string[] roots =
        [
            Path.Combine(workspacePath, "global", "agents", agent, "skills"),
            Path.Combine(workspacePath, "projects", slug, "agents", agent, "skills"),
        ];

        foreach (var root in roots.Where(Directory.Exists))
        {
            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                var file = Path.Combine(directory, "SKILL.md");

                if (!File.Exists(file))
                {
                    continue;
                }

                try
                {
                    offered[Path.GetFileName(directory)] = File.ReadAllText(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A skill that cannot be read is one the launch will not
                    // hand over either, so leaving it out keeps the count and
                    // the launch saying the same thing.
                }
            }
        }

        return offered;
    }

    /// <summary>
    /// What the skills cost a session that never invokes one, in bytes.
    /// </summary>
    /// <remarks>
    /// The frontmatter only. An agent keeps every skill's name and description
    /// in front of itself so it can tell when one applies, and reads the body
    /// when somebody asks for it — so the standing price is the description and
    /// the rest is paid on use. Counting whole files would overstate a launch
    /// several times over, which is its own kind of wrong number.
    /// </remarks>
    public static long StandingBytes(IReadOnlyDictionary<string, string> offered)
    {
        ArgumentNullException.ThrowIfNull(offered);

        long total = 0;

        foreach (var content in offered.Values)
        {
            var opened = content.IndexOf("---", StringComparison.Ordinal);

            var closed = opened < 0
                ? -1
                : content.IndexOf("\n---", opened + 3, StringComparison.Ordinal);

            total += closed > opened
                ? System.Text.Encoding.UTF8.GetByteCount(content[opened..(closed + 4)])
                : System.Text.Encoding.UTF8.GetByteCount(content);
        }

        return total;
    }

    /// <summary>Writes the specialist as a <c>SKILL.md</c>.</summary>
    /// <remarks>
    /// The description carries the phrases the specialist already declares,
    /// because that string is what the agent matches a request against — the
    /// summary alone says what the skill is and not when to reach for it, and
    /// the phrases were written to answer exactly that.
    /// </remarks>
    public static string Render(SpecialistDocument specialist, string command)
    {
        ArgumentNullException.ThrowIfNull(specialist);

        var description = new StringBuilder(specialist.Summary.TrimEnd());

        if (specialist.Activation.TaskPhraseList.Count > 0)
        {
            if (description.Length > 0 && description[^1] != '.')
            {
                description.Append('.');
            }

            description
                .Append(" Use when the request sounds like: ")
                .Append(string.Join(", ", specialist.Activation.TaskPhraseList))
                .Append('.');
        }

        var text = new StringBuilder();

        text.Append("---\n");
        text.Append("name: ").Append(command).Append('\n');

        // Quoted and escaped, because a summary is prose and prose contains
        // colons. An unquoted one would be read as a mapping and the skill
        // would be refused for a reason nobody would connect to the wording.
        text.Append("description: ")
            .Append('"')
            .Append(description.ToString().Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal))
            .Append('"')
            .Append('\n');

        text.Append("---\n\n");
        text.Append("# ").Append(specialist.Title).Append("\n\n");
        text.Append(specialist.Body.TrimStart('\n'));

        if (!specialist.Body.EndsWith('\n'))
        {
            text.Append('\n');
        }

        return text.ToString();
    }
}
