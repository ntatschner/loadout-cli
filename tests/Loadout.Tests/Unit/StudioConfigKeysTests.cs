using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Core.Configuration;
using Loadout.Models.Configuration;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Studio's settings, as 'loadout config set' takes them: the values each
/// allows, an empty value unsetting it, a layout checked before it is kept,
/// since the key can be set by hand, and the map's three filters, which
/// replaced studio-map-scope.
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
    [InlineData("studio-map-kinds", "runs, sessions")]
    [InlineData("studio-map-kinds", "runs, sessions, ideas, tasks")]
    [InlineData("studio-map-states", "waiting")]
    [InlineData("studio-map-projects", "starstats, (no project)")]
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
    [InlineData("studio-map-kinds", "boats")]
    [InlineData("studio-map-kinds", "runs, team runs")]
    [InlineData("studio-map-states", "asleep")]
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
    [InlineData("studio-map-kinds")]
    [InlineData("studio-map-states")]
    [InlineData("studio-map-projects")]
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

    [Theory]
    [InlineData("studio-map-kinds", "Tasks,runs, tasks", "runs, tasks")]
    [InlineData("studio-map-states", " ended ,waiting", "waiting, ended")]
    [InlineData("studio-map-projects", "website,(no project) , website", "website, (no project)")]
    public void A_filter_is_kept_tidy_each_word_once_kinds_and_states_in_the_bars_order(string key, string value, string kept)
    {
        var (config, machine) = Fresh();
        var entry = ConfigKeys.Find(key)!;

        entry.Write(config, machine, value);

        entry.Read(config, machine).Should().Be(kept);
    }

    [Fact]
    public void The_old_scope_is_retired_and_says_which_keys_took_its_place()
    {
        ConfigKeys.Find("studio-map-scope").Should().BeNull("it is no longer a setting anyone can set");

        // Through the message get and set give for a key they cannot find,
        // so 'loadout config set studio-map-scope all' fails saying this.
        var said = ConfigGetCommand.UnknownKeyMessage("studio-map-scope");

        said.Should().StartWith("'studio-map-scope' has been retired", "the list of every key there is would name the new ones too, without saying they replaced it");
        said.Should().Contain("studio-map-kinds").And.Contain("studio-map-states").And.Contain("studio-map-projects");
        ConfigKeys.Retired("studio-halo").Should().BeNull();
    }

    [Fact]
    public void A_scope_saved_as_all_is_read_once_as_everything_and_not_written_back()
    {
        var yaml = new YamlStore(new Fakes.NoOpFilePermissions());

        var config = yaml.Parse<LauncherConfig>("""
            studio:
              map_scope: all
            """).Value!;

        config.Studio.MapKinds.Should().Be("runs, sessions, ideas, tasks");
        config.Studio.MapStates.Should().Be("waiting, running, failed, ended");
        config.Studio.MapProjects.Should().BeNull();
        yaml.Render(config).Should().NotContain("map_scope: all", "the old key is retired once the new ones hold what it said");
    }

    [Fact]
    public void A_scope_saved_as_inflight_leaves_the_filters_unset()
    {
        var config = new YamlStore(new Fakes.NoOpFilePermissions()).Parse<LauncherConfig>("""
            studio:
              map_scope: inflight
            """).Value!;

        config.Studio.MapKinds.Should().BeNull();
        config.Studio.MapStates.Should().BeNull();
    }

    [Fact]
    public void A_filter_already_set_wins_over_the_old_scope()
    {
        var config = new YamlStore(new Fakes.NoOpFilePermissions()).Parse<LauncherConfig>("""
            studio:
              map_kinds: runs
              map_scope: all
            """).Value!;

        config.Studio.MapKinds.Should().Be("runs");
        config.Studio.MapStates.Should().Be("waiting, running, failed, ended");
    }

    private static (LauncherConfig Config, MachineConfig Machine) Fresh() => (new LauncherConfig(), new MachineConfig());
}
