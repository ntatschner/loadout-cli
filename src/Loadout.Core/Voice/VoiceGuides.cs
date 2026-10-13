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

    /// <summary>Every guide that ships, by id.</summary>
    public static IReadOnlyList<VoiceGuide> All() => [Prose()];

    /// <summary>A guide by id, or null.</summary>
    public static VoiceGuide? Find(string id) =>
        All().FirstOrDefault(g => string.Equals(g.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));
}
