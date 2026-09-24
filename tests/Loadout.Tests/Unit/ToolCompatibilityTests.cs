using FluentAssertions;
using Loadout.Core.Tools;
using Loadout.Models.Tools;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>Whether a version breaks the callers of the one before it.</summary>
public sealed class ToolCompatibilityTests
{
    [Fact]
    public void Removing_a_required_input_is_breaking()
    {
        var previous = ToolStoreFixture.Manifest("free-cache", "1.0");
        var next = ToolStoreFixture.Manifest("free-cache", "1.1");
        next.Inputs = [new ToolInput { Name = "Path", Type = "path", Required = true, Default = "." }];

        ToolCompatibility.Breaks(previous, next).Should().Contain(one => one.Contains("CachePath", StringComparison.Ordinal));
        ToolCompatibility.Refusal(previous, next).Should().NotBeNull();

        next.Version = "2.0";
        next.Compatibility = new ToolCompatibilityInfo { Breaks = true, Migration = "Pass -Path instead of -CachePath." };

        ToolCompatibility.Refusal(previous, next).Should().BeNull();
    }

    [Fact]
    public void An_unchanged_interface_is_not_breaking()
    {
        ToolCompatibility.Breaks(
            ToolStoreFixture.Manifest("free-cache", "1.0"),
            ToolStoreFixture.Manifest("free-cache", "1.1")).Should().BeEmpty();
    }
}
