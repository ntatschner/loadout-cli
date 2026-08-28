using System.Globalization;
using System.Text;
using Loadout.Models;
using Loadout.Models.Instructions;
using Loadout.Models.Results;

namespace Loadout.Core.Instructions;

/// <summary>A specialist file that has been drafted but not yet written.</summary>
/// <param name="Id">The identifier, which is also the address it is reached by.</param>
/// <param name="Kind">The layer it belongs to, taken from the identifier.</param>
/// <param name="FileName">What to call the file, including the extension.</param>
/// <param name="Content">The file, frontmatter and body.</param>
public sealed record SpecialistDraft(
    string Id,
    SpecialistKind Kind,
    string FileName,
    string Content);

/// <summary>
/// Writes the first draft of a specialist, so that adding one does not begin
/// with remembering a frontmatter vocabulary.
/// </summary>
/// <remarks>
/// <para>
/// The library has always been extensible — a workspace or a project can add
/// specialists and they are loaded with their provenance shown. There was no
/// way to make one. Somebody had to know the ten frontmatter keys, know which
/// of them their layer uses, know that the identifier and the kind have to
/// agree, know which directory the file belongs in, and find out they were
/// wrong by running validate afterwards.
/// </para>
/// <para>
/// So the draft is written already valid, and carries only the activation
/// fields its layer can actually use: a language is found by the files in a
/// repository, a skill by the words in a task, and a foundation applies always
/// and needs no activation at all. Fields that would be inert are left out
/// rather than commented out, because a commented field is an invitation to
/// fill in something that will never be read.
/// </para>
/// </remarks>
public static class SpecialistScaffold
{
    /// <summary>
    /// Drafts a specialist from an identifier of the form <c>kind.name</c>.
    /// </summary>
    public static OperationResult<SpecialistDraft> Draft(
        string id,
        string? title = null,
        string? summary = null)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return OperationResult<SpecialistDraft>.Fail(
                "No identifier was given. One looks like 'skill.deploy-checklist'.",
                ExitCode.InvalidArguments);
        }

        var trimmed = id.Trim();
        var separator = trimmed.IndexOf('.', StringComparison.Ordinal);

        if (separator <= 0 || separator == trimmed.Length - 1)
        {
            return OperationResult<SpecialistDraft>.Fail(
                $"'{trimmed}' is not an identifier. One names its layer first, "
                + "like 'skill.deploy-checklist' or 'language.rust'.",
                ExitCode.InvalidArguments);
        }

        var kindText = trimmed[..separator];
        var name = trimmed[(separator + 1)..];

        if (!Enum.TryParse<SpecialistKind>(kindText, ignoreCase: true, out var kind))
        {
            var kinds = string.Join(", ", Enum.GetNames<SpecialistKind>().Select(k => k.ToLowerInvariant()));

            return OperationResult<SpecialistDraft>.Fail(
                $"'{kindText}' is not a layer. The layers are: {kinds}.",
                ExitCode.InvalidArguments);
        }

        if (name.Any(char.IsWhiteSpace))
        {
            return OperationResult<SpecialistDraft>.Fail(
                $"'{name}' cannot contain spaces. Identifiers are addresses, so they are "
                + "hyphenated: 'skill.deploy-checklist' rather than 'skill.deploy checklist'.",
                ExitCode.InvalidArguments);
        }

        var canonical = $"{kind.ToString().ToLowerInvariant()}.{name.ToLowerInvariant()}";

        var content = Compose(
            canonical,
            kind,
            name,
            string.IsNullOrWhiteSpace(title) ? Humanise(name) : title.Trim(),
            string.IsNullOrWhiteSpace(summary) ? SummaryFor(kind) : summary.Trim());

        return OperationResult<SpecialistDraft>.Ok(
            new SpecialistDraft(canonical, kind, $"{name.ToLowerInvariant()}.md", content));
    }

    /// <summary>
    /// The directory a specialist of this kind belongs in, under a library root.
    /// </summary>
    /// <remarks>
    /// One directory per layer, matching how the built-in library is arranged,
    /// so somebody reading either can see the shape of the model from the
    /// filesystem alone.
    /// </remarks>
    public static string DirectoryFor(SpecialistKind kind) => kind.ToString().ToLowerInvariant();

    private static string Compose(
        string id,
        SpecialistKind kind,
        string name,
        string title,
        string summary)
    {
        var text = new StringBuilder();

        text.AppendLine("---");
        text.AppendLine(CultureInfo.InvariantCulture, $"id: {id}");
        text.AppendLine(CultureInfo.InvariantCulture, $"kind: {kind.ToString().ToLowerInvariant()}");
        text.AppendLine(CultureInfo.InvariantCulture, $"title: {title}");
        text.AppendLine(CultureInfo.InvariantCulture, $"summary: {summary}");

        AppendActivation(text, kind, name);

        text.AppendLine("---");
        text.AppendLine();

        AppendBody(text, kind, title);

        return text.ToString();
    }

    /// <summary>
    /// The activation a layer can actually use, and nothing else.
    /// </summary>
    private static void AppendActivation(StringBuilder text, SpecialistKind kind, string name)
    {
        switch (kind)
        {
            case SpecialistKind.Foundation:
                // Foundations are the floor: they apply to everything, so there
                // is nothing to decide.
                text.AppendLine("always: true");
                break;

            case SpecialistKind.Mode:
                // Nothing. A mode is reached by its own name — asking for
                // 'review' loads 'mode.review' — so it needs no activation to
                // be found.
                //
                // Not a 'modes:' list, which means the opposite: it restricts a
                // specialist to the modes it applies in. Putting one here would
                // scope the review mode to review mode, which is true, useless,
                // and teaches the next person the wrong thing about the field.
                break;

            case SpecialistKind.Language:
            case SpecialistKind.Framework:
            case SpecialistKind.Database:
            case SpecialistKind.Platform:
            case SpecialistKind.Cloud:
                // Found by what is in the repository. Both are listed because
                // one alone is usually too loose: a file extension can belong to
                // more than one thing, and a dependency is only named once.
                text.AppendLine("globs:");
                text.AppendLine("  - '**/*.example'");
                text.AppendLine("dependencies:");
                text.AppendLine(CultureInfo.InvariantCulture, $"  - {name}");
                break;

            default:
                // Functions and skills are chosen by what somebody asked for,
                // so they are found by the words of the task.
                text.AppendLine("task_phrases:");
                text.AppendLine(CultureInfo.InvariantCulture, $"  - '{Humanise(name).ToLowerInvariant()}'");
                break;
        }
    }

    private static void AppendBody(StringBuilder text, SpecialistKind kind, string title)
    {
        if (kind is SpecialistKind.Skill)
        {
            text.AppendLine("## When to use");
            text.AppendLine();
            text.AppendLine(CultureInfo.InvariantCulture, $"Describe the situation that calls for {title}.");
            text.AppendLine();
            text.AppendLine("## Procedure");
            text.AppendLine();
            text.AppendLine("1. The first step, written as an instruction.");
            text.AppendLine("2. The next one.");
            text.AppendLine();
            text.AppendLine("## When to stop");
            text.AppendLine();
            text.AppendLine("What finishing looks like, so the agent knows it is done.");

            return;
        }

        if (kind is SpecialistKind.Mode)
        {
            text.AppendLine("## How to work");
            text.AppendLine();
            text.AppendLine("What this mode changes about the approach. Chosen by name, so a launch");
            text.AppendLine("asking for this mode loads it and nothing else has to point at it.");
            text.AppendLine();
            text.AppendLine("Say what it rules out as well as what it asks for. A mode that only adds");
            text.AppendLine("is one an agent can satisfy while doing what it was going to anyway.");

            return;
        }

        text.AppendLine("## Guidance");
        text.AppendLine();
        text.AppendLine("What an agent should know, written as instructions rather than description.");
        text.AppendLine();
        text.AppendLine("Everything here is paid for in context on every launch that selects it, so");
        text.AppendLine("prefer the things that are not obvious from reading the code.");
    }

    /// <summary>Turns a hyphenated identifier into something a person would write.</summary>
    private static string Humanise(string name)
    {
        var words = name.Replace('-', ' ').Replace('_', ' ').Trim();

        if (words.Length == 0)
        {
            return name;
        }

        return char.ToUpperInvariant(words[0]) + words[1..];
    }

    private static string SummaryFor(SpecialistKind kind) => kind switch
    {
        SpecialistKind.Foundation => "Applies to every task.",
        SpecialistKind.Mode => "How to work when this mode is chosen.",
        SpecialistKind.Language => "Working in this language.",
        SpecialistKind.Framework => "Working with this framework.",
        SpecialistKind.Database => "Working against this database.",
        SpecialistKind.Platform => "Working on this platform.",
        SpecialistKind.Cloud => "Working with this provider.",
        SpecialistKind.Function => "Guidance for this area of work.",
        _ => "A procedure to follow.",
    };
}
