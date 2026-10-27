using System.Text.Json;
using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Models.Configuration;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Studio's settings as the page reads them: what is set, and the default
/// where nothing is.
/// </summary>
public sealed class StudioPreferencesTests
{
    [Fact]
    public void An_unset_backdrop_is_read_as_plain()
    {
        Read(new StudioSettings()).GetProperty("backdrop").GetString().Should().Be("plain");
        Read(null).GetProperty("backdrop").GetString().Should().Be("plain");
    }

    [Fact]
    public void A_set_backdrop_is_read_as_it_was_set()
    {
        Read(new StudioSettings { Backdrop = "map-animated" }).GetProperty("backdrop").GetString().Should().Be("map-animated");
    }

    /// <summary>The preferences as the page receives them, over JSON.</summary>
    private static JsonElement Read(StudioSettings? studio) =>
        JsonSerializer.SerializeToElement(StudioPreferences.Read(studio));
}
