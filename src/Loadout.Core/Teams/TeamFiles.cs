using Loadout.Models.Instructions;

namespace Loadout.Core.Teams;

/// <summary>
/// Teams of your own, as files: where one goes, what a new one starts as, and
/// which of them you are allowed to change.
/// </summary>
/// <remarks>
/// <para>
/// Reading teams is <see cref="TeamCatalogue"/>'s and stays there. This is the
/// other direction, and it is deliberately thin: a team is a YAML file, the
/// point of the commands is that you can then open it, and anything here that
/// started generating YAML from answers to questions would be a second way of
/// describing a team that has to be kept in step with the first.
/// </para>
/// <para>
/// So <c>team new</c> writes either the smallest file that loads, or a copy of
/// one that already does. Both then go through the same parser and the same
/// checks as any other team, which is the whole reason this writes text rather
/// than objects.
/// </para>
/// </remarks>
public static class TeamFiles
{
    /// <summary>
    /// Whether that can be a team's name.
    /// </summary>
    /// <remarks>
    /// Lowercase, hyphenated, as the built-ins are — and checked here because
    /// the name becomes a file name. The same reasoning as a run identifier:
    /// everything downstream joins it to a path, so a name carrying a
    /// separator or a <c>..</c> writes a team somewhere that is not the
    /// teams directory.
    /// </remarks>
    public static bool Names(string? name) =>
        name is { Length: > 0 and <= 64 }
        && name.All(one => char.IsAsciiLetterLower(one) || char.IsAsciiDigit(one) || one == '-')
        && name[0] != '-'
        && name[^1] != '-';

    /// <summary>The directory a team of yours is written to.</summary>
    /// <param name="workspaceRoot">The workspace.</param>
    /// <param name="slug">The project, for a team only that project should see, or null for all of them.</param>
    public static string DirectoryFor(string workspaceRoot, string? slug) =>
        slug is { Length: > 0 }
            ? Path.Combine(workspaceRoot, "projects", slug, "teams")
            : Path.Combine(workspaceRoot, "global", "teams");

    /// <summary>
    /// Why this team cannot be changed from here, or null where it can.
    /// </summary>
    /// <remarks>
    /// Two of them are refusals with a way forward rather than dead ends, and
    /// the sentence says it: a team that ships and a team from a pack are both
    /// somebody else's file, and copying it into your workspace is exactly
    /// what <c>team new --from</c> is for. Refusing without naming that is how
    /// somebody ends up editing a file inside a pack checkout, which the next
    /// <c>pack update</c> overwrites.
    /// </remarks>
    /// <param name="name">The team.</param>
    /// <param name="origin">Which layer its surviving definition came from.</param>
    /// <param name="source">The file it came from, or null where the catalogue did not say.</param>
    public static string? Whyever(string name, SpecialistOrigin origin, string? source) =>
        origin switch
        {
            SpecialistOrigin.BuiltIn =>
                $"'{name}' ships with Loadout. Make it yours first with: "
                + $"loadout team new {name}-mine --from {name}",
            SpecialistOrigin.Pack =>
                $"'{name}' comes from a pack, and the next 'pack update' would overwrite anything "
                + $"you changed. Make it yours first with: loadout team new {name}-mine --from {name}",
            _ when source is not { Length: > 0 } =>
                $"Nothing recorded where '{name}' was read from, so there is no file to change.",
            _ => null,
        };

    /// <summary>
    /// The smallest team file that loads, checks and does something.
    /// </summary>
    /// <remarks>
    /// A lead and one worker rather than a lead alone, because a team of one
    /// is a session with extra steps and copying this is how somebody finds
    /// out what the shape is. The roles are two that always exist, so the file
    /// this writes passes <c>team show</c> on a machine with nothing else set
    /// up.
    /// </remarks>
    public static string Scaffold(string name) =>
        $"""
        name: {name}
        description: Say here what a run of this does, in a sentence.

        # What this team is for, whatever any one run is about. It reaches every
        # node's brief, so a worker given a narrow job still knows the point of it.
        goal: ''

        # How this team works, as rules somebody could check it against
        # afterwards. A declaration nobody can check is a hope.
        declarations: []

        lead: lead

        nodes:
          lead:
            role: role.project-lead
            delegates: [reviewer]
          reviewer:
            role: role.reviewer

        rules:
          # manual, supervised or autonomous.
          autonomy: supervised
          gates:
            # Every outward action is held for you. A team file may not allow
            # one by itself.
            outward: ask

        """;

    /// <summary>
    /// One team's text under another name, ready to be a team of yours.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The text is copied rather than the definition re-serialised, so the
    /// comments, the ordering and the inline maps survive. A copy that came
    /// back sorted alphabetically with every comment gone would be a worse
    /// starting point than the file somebody was reading when they decided to
    /// copy it.
    /// </para>
    /// <para>
    /// Two lines are changed, both top level: the name, and the template flag.
    /// A template is a shape to copy and <c>team run</c> refuses one, so a
    /// copy that kept the flag would be a team you had just made and could not
    /// run. Matched at the start of a line with no indentation, because
    /// <c>name:</c> under a node is that node's business.
    /// </para>
    /// </remarks>
    public static string Renamed(string text, string name)
    {
        ArgumentNullException.ThrowIfNull(text);

        var lines = text.ReplaceLineEndings("\n").Split('\n');
        var written = new List<string>(lines.Length);
        var named = false;

        foreach (var line in lines)
        {
            if (!named && line.StartsWith("name:", StringComparison.Ordinal))
            {
                written.Add($"name: {name}");
                named = true;

                continue;
            }

            if (line.StartsWith("template:", StringComparison.Ordinal))
            {
                continue;
            }

            written.Add(line);
        }

        // A file that never said its name is not one this could have been
        // asked about - the catalogue keys teams by the name inside them - but
        // saying so in the file beats writing a team nothing can find.
        if (!named)
        {
            written.Insert(0, $"name: {name}");
        }

        return string.Join(Environment.NewLine, written);
    }
}
