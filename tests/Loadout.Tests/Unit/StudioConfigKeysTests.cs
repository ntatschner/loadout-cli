using FluentAssertions;
using Loadout.Core.Configuration;
using Loadout.Models.Configuration;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Studio's nine settings, as 'loadout config set' takes them: the values
/// each allows, an empty value unsetting it, and a layout checked before it is
/// kept, since the key can be set by hand.
/// </summary>
public sealed class StudioConfigKeysTests
{
    [Theory]
    [InlineData("studio-halo", "gyroscope")]
    [InlineData("studio-theme", "hc-light")]
    [InlineData("studio-accent", "violet")]
    [InlineData("studio-density", "compact")]
    [InlineData("studio-motion", "none")]
    [InlineData("studio-backdrop", "plain")]
    [InlineData("studio-backdrop", "map")]
    [InlineData("studio-backdrop", "map-animated")]
    [InlineData("studio-idle-after", "25")]
    [InlineData("studio-map-scope", "all")]
    [InlineData("studio-map-scope", "inflight")]
    public void Each_takes_the_values_it_offers_and_reads_them_back(string key, string value)
    {
        var (config, machine) = Fresh();
        var entry = ConfigKeys.Find(key)!;

        entry.Write(config, machine, value);

        entry.Read(config, machine).Should().Be(value);
        entry.IsMachineLocal.Should().BeFalse("how Studio looks follows the person to another machine with the workspace");
    }

    [Theory]
    [InlineData("studio-halo", "spiral")]
    [InlineData("studio-theme", "solarised")]
    [InlineData("studio-accent", "orange")]
    [InlineData("studio-density", "tight")]
    [InlineData("studio-motion", "some")]
    [InlineData("studio-backdrop", "background")]
    [InlineData("studio-backdrop", "map-scan")]
    [InlineData("studio-idle-after", "0")]
    [InlineData("studio-idle-after", "1441")]
    [InlineData("studio-idle-after", "ten")]
    [InlineData("studio-map-scope", "everything")]
    public void A_value_it_does_not_offer_is_refused(string key, string value)
    {
        var (config, machine) = Fresh();

        var setting = () => ConfigKeys.Find(key)!.Write(config, machine, value);

        setting.Should().Throw<FormatException>();
    }

    [Theory]
    [InlineData("studio-halo")]
    [InlineData("studio-theme")]
    [InlineData("studio-backdrop")]
    [InlineData("studio-layout-lens")]
    [InlineData("studio-idle-after")]
    [InlineData("studio-map-scope")]
    public void An_empty_value_unsets_it(string key)
    {
        var (config, machine) = Fresh();
        var entry = ConfigKeys.Find(key)!;
        entry.Write(config, machine, entry.Sample!);

        entry.Write(config, machine, "");

        entry.Read(config, machine).Should().BeEmpty();
    }

    [Fact]
    public void A_layout_as_studio_saves_it_is_kept()
    {
        var (config, machine) = Fresh();
        const string saved = """{"order":["all","needs","focus","start","signal"],"hidden":["signal"]}""";

        ConfigKeys.Find("studio-layout-lens")!.Write(config, machine, saved);

        config.Studio.LayoutLens.Should().Be(saved);
    }

    [Theory]
    [InlineData("studio-layout-lens", "not json")]
    [InlineData("studio-layout-lens", "[]")]
    [InlineData("studio-layout-lens", """{"order":["needs","needs"]}""")]
    [InlineData("studio-layout-lens", """{"order":["status"]}""")]
    [InlineData("studio-layout-lens", """{"order":["needs"],"where":{}}""")]
    [InlineData("studio-layout-map", """{"hidden":["needs"]}""")]
    [InlineData("studio-layout-map", """{"hidden":"map"}""")]
    public void A_layout_naming_what_its_view_does_not_have_is_refused(string key, string value)
    {
        var (config, machine) = Fresh();

        var setting = () => ConfigKeys.Find(key)!.Write(config, machine, value);

        setting.Should().Throw<FormatException>();
    }

    private static (LauncherConfig Config, MachineConfig Machine) Fresh() => (new LauncherConfig(), new MachineConfig());
}
