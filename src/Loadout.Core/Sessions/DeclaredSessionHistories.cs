using Loadout.Models.Configuration;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Sessions;

/// <summary>The session readers that come from configuration rather than code.</summary>
internal interface IDeclaredSessionHistories
{
    /// <summary>One reader per custom agent that describes where its transcripts are.</summary>
    IReadOnlyList<ISessionHistory> All { get; }
}

/// <summary>
/// Builds a session reader for every custom agent that says where its
/// transcripts live.
/// </summary>
/// <remarks>
/// <para>
/// A separate collection rather than more registrations of
/// <see cref="ISessionHistory"/>, because how many there are is only known once
/// configuration has been read, and the container is built before that. The
/// service that gathers sessions takes both and joins them.
/// </para>
/// <para>
/// A described agent that takes the name of a compiled-in one replaces it. That
/// is the point rather than an accident: these formats are undocumented and
/// change without notice, so when one breaks, correcting it in
/// <c>config.yaml</c> is a fix somebody can apply the same afternoon instead of
/// waiting for a release. It also stops the two readers listing every session
/// twice.
/// </para>
/// </remarks>
internal sealed class DeclaredSessionHistories : IDeclaredSessionHistories
{
    public DeclaredSessionHistories(LauncherConfig config, IEnvironmentProvider environment)
    {
        ArgumentNullException.ThrowIfNull(config);

        All = config.CustomAgents
            .Where(entry => entry.Value?.Transcripts is { } format && format.IsUsable)
            .Select(entry => (ISessionHistory)new DeclaredSessionHistory(
                entry.Key,
                entry.Value.Transcripts!,
                environment))
            .ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<ISessionHistory> All { get; }
}
