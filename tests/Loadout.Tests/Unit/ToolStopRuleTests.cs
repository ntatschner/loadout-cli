using FluentAssertions;
using Loadout.Models.Tools;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>When the Refiner stops looking at a tool.</summary>
public sealed class ToolStopRuleTests : IDisposable
{
    private const string Script = "param([string]$CachePath)\nGet-ChildItem $CachePath | Remove-Item\n";

    private readonly ToolStoreFixture _store = new();

    public void Dispose() => _store.Dispose();

    [Fact]
    public async Task Two_stand_downs_without_new_signal_skip_the_tool()
    {
        var (registry, _) = _store.Registry();
        await _store.PromoteAsync(registry, ToolStoreFixture.Manifest("free-cache", "1.0"), Script, ToolStoreFixture.Cases());

        registry.NeedsRefining("free-cache").Should().BeTrue();

        registry.StandDown("free-cache", "Nothing measurably better.");
        registry.NeedsRefining("free-cache").Should().BeTrue();

        // An ordinary successful use is not something new to refine against.
        registry.RecordUsage(new ToolUsage { Tool = "free-cache", Version = "1.0", Outcome = ToolOutcome.Ok, Team = "t" });
        registry.StandDown("free-cache", "Still nothing.");
        registry.NeedsRefining("free-cache").Should().BeFalse();

        registry.RecordUsage(new ToolUsage { Tool = "free-cache", Version = "1.0", Outcome = ToolOutcome.Failed, Team = "t" });
        registry.NeedsRefining("free-cache").Should().BeTrue();
    }
}
