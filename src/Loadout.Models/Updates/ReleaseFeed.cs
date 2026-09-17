namespace Loadout.Models.Updates;

/// <summary>
/// What a release source publishes (spec section 79).
/// <para>
/// Deliberately a small, self-describing document so the source can be a static
/// file on any web server, or a path on a network share. Spec section 79 wants
/// an internal or self-hosted release source to be as ordinary as a public one,
/// and that rules out anything needing a service to answer.
/// </para>
/// </summary>
public sealed class ReleaseFeed
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Version being offered, for example <c>0.2.0</c>.</summary>
    public string Version { get; set; } = string.Empty;

    public DateTimeOffset? Released { get; set; }

    /// <summary>Optional release notes URL or text shown before updating.</summary>
    public string? Notes { get; set; }

    /// <summary>
    /// One entry per runtime identifier, for example <c>osx-arm64</c>. A feed
    /// that omits a platform simply has no update for it, which is not an error.
    /// </summary>
    public Dictionary<string, ReleaseArtifact> Artifacts { get; set; } = [];
}

/// <summary>One downloadable build.</summary>
public sealed class ReleaseArtifact
{
    /// <summary>Absolute URL, or a path when the source is a local directory.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Lowercase hex SHA-256 of the archive.
    /// <para>
    /// Required. The launcher replaces its own executable from this download,
    /// so a feed that does not commit to a hash is a feed that can hand over
    /// anything at all.
    /// </para>
    /// </summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>Expected size in bytes, when the feed states one.</summary>
    public long? Size { get; set; }
}

/// <summary>The result of asking a release source what it has.</summary>
/// <param name="CurrentVersion">The version running now.</param>
/// <param name="AvailableVersion">What the feed offers, or null when it offers nothing for this platform.</param>
/// <param name="IsNewer">Whether the offered version is actually newer than the running one.</param>
/// <param name="Artifact">The build for this platform, when there is one.</param>
/// <param name="Notes">Release notes, when the feed carries any.</param>
public sealed record UpdateCheck(
    string CurrentVersion,
    string? AvailableVersion,
    bool IsNewer,
    ReleaseArtifact? Artifact,
    string? Notes);

/// <summary>
/// Where <c>loadout update</c> looks, and what it means to say "nowhere".
/// </summary>
/// <remarks>
/// Here rather than on the updater because three assemblies need the same
/// answer: the service that reads the feed, first-run setup that offers it, and
/// the configuration key that describes it.
/// </remarks>
public static class ReleaseSource
{
    /// <summary>
    /// Where updates come from when nobody has said otherwise: this project's
    /// own releases.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A default rather than a required setting, because the setting being
    /// empty is the state every install starts in and the command's answer to
    /// it was "no release source is configured" — so `loadout update` did
    /// nothing at all until somebody found a URL to give it, and there was no
    /// URL to find: the release published archives and a manifest, never a
    /// feed. Both halves are fixed together or neither is.
    /// </para>
    /// <para>
    /// `/releases/latest/download/` always redirects to the newest release, so
    /// this URL does not need updating when one is cut.
    /// </para>
    /// <para>
    /// Nothing reads this on its own account: the only caller is
    /// <c>loadout update</c>, so the network is touched when somebody asks and
    /// at no other time.
    /// </para>
    /// </remarks>
    public const string Default =
        "https://github.com/ntatschner/loadout-cli/releases/latest/download/feed.json";

    /// <summary>
    /// What <c>updates-source</c> is set to by somebody who wants no update
    /// checking at all.
    /// </summary>
    /// <remarks>
    /// Spelled several ways because the setting is typed by hand and refusing
    /// all but one spelling would be pedantry. Distinct from empty, which now
    /// means the default: turning something off has to be something you can
    /// say, not something you say by deleting.
    /// </remarks>
    private static readonly string[] Off = ["off", "none", "no", "disabled", "false"];

    /// <summary>Whether a configured value means "do not check at all".</summary>
    public static bool IsDisabled(string? source) =>
        source is not null
        && Off.Contains(source.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The feed a configured value points at, with empty meaning the default.
    /// </summary>
    public static string Resolve(string? source) =>
        string.IsNullOrWhiteSpace(source) ? Default : source.Trim();
}
