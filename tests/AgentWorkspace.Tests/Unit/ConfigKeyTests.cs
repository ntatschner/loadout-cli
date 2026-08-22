using AgentWorkspace.Cli.Commands;
using AgentWorkspace.Models.Configuration;
using FluentAssertions;
using Xunit;

namespace AgentWorkspace.Tests.Unit;

/// <summary>
/// The settings registry behind <c>agentctl config</c> (spec section 77).
/// <para>
/// Worth testing as a set rather than one key at a time: the failure mode is a
/// key that reads from one place and writes to another, which no individual
/// assertion would catch but a round trip over every key does.
/// </para>
/// </summary>
public sealed class ConfigKeyTests
{
    public static TheoryData<string> EveryKey =>
        [.. ConfigKeys.All.Select(e => e.Key)];

    [Theory]
    [MemberData(nameof(EveryKey))]
    public void Every_setting_reads_back_what_was_written(string key)
    {
        var entry = ConfigKeys.Find(key)!;
        var config = new LauncherConfig();
        var machine = new MachineConfig();

        // Numeric settings need a value they can parse; everything else takes
        // an arbitrary string.
        var value = key == "sync-timeout" ? "42" : "round-trip-value";

        entry.Write(config, machine, value);

        entry.Read(config, machine).Should().Be(value);
    }

    [Theory]
    [MemberData(nameof(EveryKey))]
    public void Every_setting_has_an_explanation(string key)
    {
        // The description is what `config list` shows and what an unknown-key
        // error suggests, so an empty one leaves the user guessing.
        ConfigKeys.Find(key)!.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Setting_names_are_matched_without_regard_to_case()
    {
        ConfigKeys.Find("DEFAULT-AGENT").Should().NotBeNull();
        ConfigKeys.Find("default-agent").Should().NotBeNull();
    }

    [Fact]
    public void An_unknown_setting_is_not_found()
    {
        ConfigKeys.Find("no-such-setting").Should().BeNull();

        // The error names the alternatives, which turns a typo into a one-step
        // fix rather than a trip to the documentation.
        ConfigGetCommand.UnknownKeyMessage("no-such-setting")
            .Should().Contain("default-agent");
    }

    [Fact]
    public void Machine_local_settings_are_marked_as_such()
    {
        // Spec section 15: absolute paths describe one machine and must never
        // travel. Mislabelling one would put a Windows path into the shared
        // configuration.
        ConfigKeys.Find("clone-root")!.IsMachineLocal.Should().BeTrue();
        ConfigKeys.Find("discovery-roots")!.IsMachineLocal.Should().BeTrue();

        ConfigKeys.Find("default-agent")!.IsMachineLocal.Should().BeFalse();
        ConfigKeys.Find("workspace-remote")!.IsMachineLocal.Should().BeFalse();
    }

    [Fact]
    public void A_machine_local_setting_writes_only_to_the_machine_config()
    {
        var entry = ConfigKeys.Find("clone-root")!;
        var config = new LauncherConfig();
        var machine = new MachineConfig();

        entry.Write(config, machine, "/home/test/git");

        machine.DefaultCloneRoot.Should().Be("/home/test/git");

        // Nothing about this machine should have landed in the portable config.
        config.Workspace.Remote.Should().BeEmpty();
        config.AgentSearchPaths.Should().BeEmpty();
    }

    [Fact]
    public void A_list_setting_splits_and_trims()
    {
        var entry = ConfigKeys.Find("discovery-roots")!;
        var machine = new MachineConfig();

        entry.Write(new LauncherConfig(), machine, " /home/a , /home/b ,, /home/c ");

        machine.DiscoveryRoots.Should().Equal("/home/a", "/home/b", "/home/c");
    }
}
