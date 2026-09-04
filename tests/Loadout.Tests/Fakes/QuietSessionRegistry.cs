using Loadout.Core.Sessions;

namespace Loadout.Tests.Fakes;

/// <summary>A registry with nothing running in it.</summary>
/// <remarks>
/// The state of a machine nobody has launched anything on, which is what a test
/// about a project's overview wants. A real one would read this machine's own
/// registry and make the test depend on whether its owner happened to have a
/// session open.
/// </remarks>
public sealed class QuietSessionRegistry : ISessionRegistry
{
    /// <summary>What to report as running, if a test wants something.</summary>
    public List<RunningSession> Running { get; } = [];

    /// <inheritdoc />
    public string Path => "(none)";

    /// <inheritdoc />
    public Task RegisterAsync(NewSession session, CancellationToken ct = default) =>
        Task.CompletedTask;

    /// <inheritdoc />
    public Task ReleaseAsync(string launchId, CancellationToken ct = default) =>
        Task.CompletedTask;

    /// <inheritdoc />
    public Task<IReadOnlyList<RunningSession>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<RunningSession>>(Running);
}
