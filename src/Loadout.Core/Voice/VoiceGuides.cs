using Loadout.Models.Voice;

namespace Loadout.Core.Voice;

/// <summary>The guide definitions that ship.</summary>
public static class VoiceGuides
{
    /// <summary>A commit's message.</summary>
    public const string Commit = "commit";

    /// <summary>A pull request's description.</summary>
    public const string PullRequest = "pr";

    /// <summary>A document.</summary>
    public const string Document = "doc";

    /// <summary>What an agent says to you in a session.</summary>
    public const string Chat = "chat";

    /// <summary>What an interface shows the person using it.</summary>
    public const string Ui = "ui";

    /// <summary>
    /// How somebody writes prose: docs, commit messages and pull request
    /// descriptions, which are the surfaces the house voice already governs.
    /// </summary>
    /// <remarks>
    /// The check types are the ones that are deterministic and that the house
    /// voice already implies. Ratio checks such as passive voice need tuning
    /// before they are fit to gate, so they wait for a later version, and
    /// <c>regex</c> is an escape hatch that reports and never gates.
    /// </remarks>
    public static VoiceGuide Prose() => new()
    {
        Id = "prose",
        Version = 1,
        Summary = "How you write docs, commit messages and pull request descriptions.",
        Sources = ["squash", "mark", "edit-pair", "interview", "corpus"],
        Surfaces = [Document, Commit, PullRequest],
        CheckTypes = [.. VoiceChecker.Types],
    };

    /// <summary>
    /// How dates are written and how long work is said to take, on each
    /// surface. Set with <c>loadout voice time set</c>, never learned.
    /// </summary>
    /// <remarks>
    /// Chat and interfaces are the person's: how an agent talks to you is
    /// yours to decide whichever codebase it is working in, so there your
    /// setting wins over the codebase's. Neither has text the checker sees, so
    /// on those two surfaces the settings are guidance only.
    /// </remarks>
    public static VoiceGuide Time() => new()
    {
        Id = "time",
        Version = 1,
        Summary = "How dates are written and how long work is said to take.",
        Sources = [VoiceTime.SettingSource],
        Surfaces = [Document, Commit, PullRequest, Chat, Ui],
        PersonalSurfaces = [Chat, Ui],
        CheckTypes = [.. VoiceChecker.TimeTypes],
    };

    /// <summary>Whether a guide's rules are set rather than learned, so a learn run has nothing to read for it.</summary>
    public static bool IsSet(VoiceGuide guide) =>
        guide.Sources.Count > 0 && guide.Sources.All(s => string.Equals(s, VoiceTime.SettingSource, StringComparison.Ordinal));

    /// <summary>Every guide that ships, by id.</summary>
    public static IReadOnlyList<VoiceGuide> All() => [Prose(), Time()];

    /// <summary>A guide by id, or null.</summary>
    public static VoiceGuide? Find(string id) =>
        All().FirstOrDefault(g => string.Equals(g.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));
}
