using System.Text.Json.Serialization;

namespace Loadout.Core.About;

/// <summary>
/// Everything the About page shows: the installed version, what it can do,
/// where that is explained, and the releases around it.
/// </summary>
/// <remarks>
/// One model for every surface, so <c>loadout about --json</c>, the launcher's
/// window and Studio's page cannot disagree about any of it.
/// </remarks>
public sealed record AboutModel(
    string Version,
    bool DevBuild,
    string? Available,
    bool UpdateAvailable,
    IReadOnlyList<AboutFeature> Features,
    IReadOnlyList<AboutGuide> Guides,
    AboutTimeline Timeline);

/// <summary>One row of the feature matrix.</summary>
/// <param name="Id">A stable key for the row.</param>
/// <param name="Name">What it is called in the feature list.</param>
/// <param name="Since">The release that first carried it.</param>
/// <param name="Status"><c>stable</c>, <c>preview</c> or <c>retired</c>.</param>
/// <param name="Guide">The key of the guide in <see cref="AboutModel.Guides"/> that explains it.</param>
/// <param name="Installed">Whether the running version has it.</param>
public sealed record AboutFeature(string Id, string Name, string Since, string Status, string Guide, bool Installed);

/// <summary>A guide on GitHub, named by the key features refer to it by.</summary>
public sealed record AboutGuide(string Key, string Title, string Url);

/// <summary>The releases, newest first, or why there are none.</summary>
/// <param name="Releases">Newest first.</param>
/// <param name="Empty">Why the list could not be had, when it could not.</param>
/// <param name="FetchedAt">When GitHub was last asked.</param>
/// <param name="Stale">Whether the list is a kept copy because asking again failed.</param>
public sealed record AboutTimeline(
    IReadOnlyList<AboutRelease> Releases,
    TimelineEmpty? Empty,
    DateTimeOffset? FetchedAt,
    bool Stale);

/// <summary>One release.</summary>
/// <param name="Version">The version, without the tag's leading v.</param>
/// <param name="Name">The release's title.</param>
/// <param name="Published">When it was published, when known.</param>
/// <param name="Url">Its page on GitHub, when known.</param>
/// <param name="Notes">The release notes as published, as plain text.</param>
/// <param name="Installed">False for a release newer than the running version.</param>
/// <param name="Kind"><c>feature</c> when the minor version moved, <c>fix</c> when only the patch did.</param>
public sealed record AboutRelease(
    string Version,
    string Name,
    DateTimeOffset? Published,
    string Notes,
    string Url,
    bool Installed,
    string Kind);

/// <summary>Why the timeline has no releases from GitHub.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TimelineEmpty>))]
public enum TimelineEmpty
{
    Off,
    Offline,
    NotGitHub,
    RateLimitedNoCache,
    Unreachable,
}

/// <summary>The sentence every surface shows for an empty timeline.</summary>
public static class TimelineEmptyText
{
    /// <summary>One sentence saying why there is no timeline, and what changes that.</summary>
    public static string Describe(this TimelineEmpty reason) => reason switch
    {
        TimelineEmpty.Off =>
            "Update checking is switched off on this machine, so GitHub was not asked for releases. "
            + "Turn it back on with: loadout config set updates-source \"\"",
        TimelineEmpty.Offline =>
            "Running with --offline, so GitHub was not asked for releases.",
        TimelineEmpty.NotGitHub =>
            "Updates come from a source that is not GitHub, which has no release list to show.",
        TimelineEmpty.RateLimitedNoCache =>
            "GitHub refused to answer for now (rate limited) and no earlier list is kept. Try again later.",
        TimelineEmpty.Unreachable =>
            "GitHub could not be reached and no earlier list is kept.",
        _ => "There is no release list to show.",
    };
}
