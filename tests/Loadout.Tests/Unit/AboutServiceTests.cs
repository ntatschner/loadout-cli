using FluentAssertions;
using Loadout.Core.About;
using Loadout.Core.Updates;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>The one model every About surface shows.</summary>
public sealed class AboutServiceTests
{
    [Fact]
    public async Task Model_carries_version_features_guides_and_timeline()
    {
        var release = new AboutRelease("0.110.0", "0.110.0", null, "New.", "https://github.com/x", false, "feature");
        var service = new AboutService(
            new Offering("0.110.0"),
            new Fixed(new AboutTimeline([release], null, null, false)),
            "0.109.5",
            devBuild: false);

        var about = (await service.GetAsync()).Value!;

        about.Version.Should().Be("0.109.5");
        about.Available.Should().Be("0.110.0");
        about.UpdateAvailable.Should().BeTrue();
        about.Features.Should().NotBeEmpty();
        about.Guides.Should().NotBeEmpty();
        about.Timeline.Releases.Should().ContainSingle().Which.Should().Be(release);
    }

    private sealed class Offering(string? version) : IUpdateNotice
    {
        public Task<string?> AvailableAsync(CancellationToken ct = default) => Task.FromResult(version);
    }

    private sealed class Fixed(AboutTimeline timeline) : IReleaseTimeline
    {
        public Task<AboutTimeline> ReadAsync(bool refresh, bool offline, CancellationToken ct = default) =>
            Task.FromResult(timeline);
    }
}
