using Loadout.Core.Git;
using Loadout.Models.Results;

namespace Loadout.Tests.Fakes;

/// <summary>Every project lands through the one host given, so a test chooses it rather than an origin address.</summary>
public sealed class FixedHosts(IRepositoryHost host) : IRepositoryHosts
{
    /// <inheritdoc />
    public Task<OperationResult<HostChoice>> ForAsync(string project, string repository, CancellationToken ct = default) =>
        Task.FromResult(OperationResult<HostChoice>.Ok(new HostChoice(host, "the test")));
}
