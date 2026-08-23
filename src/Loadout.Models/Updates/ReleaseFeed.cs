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
