namespace Loadout.Core.Packs;

/// <summary>Where on this machine the content of approved packs is.</summary>
/// <remarks>
/// <para>
/// One place rather than one per kind of content, because everything here is
/// the trust boundary: an active pack is one somebody read at the commit it is
/// pinned to, and a pack that is merely declared has been read by nobody. Two
/// copies of that filter is one copy that can drift into loading the wrong
/// thing.
/// </para>
/// <para>
/// The libraries that load pack content take a delegate onto this rather than
/// the pack service itself, so they stay free of remotes, approvals and Git.
/// </para>
/// </remarks>
public static class PackDirectories
{
    /// <summary>
    /// The named subdirectory of every pack this machine has approved at its
    /// pinned commit, skipping the ones that have no such directory.
    /// </summary>
    /// <param name="packs">The declared packs and their standings.</param>
    /// <param name="subdirectory">What is wanted: "specialists", "teams".</param>
    /// <param name="ct">Cancellation.</param>
    public static async Task<IReadOnlyList<string>> ApprovedAsync(
        IPackService packs,
        string subdirectory,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(packs);

        var standing = await packs.StandingAsync(ct).ConfigureAwait(false);

        if (standing.Failed)
        {
            // No packs rather than a failure. A workspace that cannot be read
            // must not stop the built-in library loading.
            return [];
        }

        return
        [
            .. standing.Value!
                .Where(entry => entry.IsActive)
                .Select(entry => packs.DirectoryFor(entry.Pack.Name))
                .Where(directory => directory is { Length: > 0 })
                .Select(directory => Path.Combine(directory!, subdirectory)),
        ];
    }
}
