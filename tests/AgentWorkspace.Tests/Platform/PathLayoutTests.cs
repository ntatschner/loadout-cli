using System.Runtime.InteropServices;
using AgentWorkspace.Models.Platform;
using AgentWorkspace.Platform.Linux;
using AgentWorkspace.Platform.MacOS;
using AgentWorkspace.Platform.Windows;
using AgentWorkspace.Tests.Fakes;
using FluentAssertions;
using Xunit;

namespace AgentWorkspace.Tests.Platform;

/// <summary>
/// Verifies the three storage layouts of spec section 16.
/// <para>
/// These run on every host because the layouts are driven entirely by an
/// injected environment. That matters: the paths are the single most
/// platform-divergent part of the launcher, and a suite that could only check
/// the layout it happened to be running on would leave two of the three
/// unverified on any given CI leg.
/// </para>
/// </summary>
public sealed class PathLayoutTests
{
    private static HostPlatform Host(HostOperatingSystem os) =>
        new(os, Architecture.Arm64, "test", "TEST-MACHINE");

    // Windows-only because the assertion is a literal Windows path. Path.Combine
    // uses whatever separator the host has, so on Linux this would be asserting
    // something about the machine running the test rather than about the layout.
    [WindowsFact]
    public void Windows_separates_roaming_configuration_from_local_state()
    {
        var environment = new FakeEnvironmentProvider(
            @"C:\Users\test",
            new Dictionary<string, string>
            {
                ["APPDATA"] = @"C:\Users\test\AppData\Roaming",
                ["LOCALAPPDATA"] = @"C:\Users\test\AppData\Local",
            });

        var paths = new WindowsPaths(environment, new NoOpFilePermissions(),
            Host(HostOperatingSystem.Windows)).Paths;

        paths.Config.Should().Be(@"C:\Users\test\AppData\Roaming\AgentWorkspaceLauncher");

        // The split is the point: machines.yaml and the workspace clone
        // describe this machine, and roaming them would carry one machine's
        // absolute paths onto another.
        paths.State.Should().Be(@"C:\Users\test\AppData\Local\AgentWorkspaceLauncher");
        paths.State.Should().NotStartWith(paths.Config);
    }

    [Fact]
    public void Linux_honours_each_xdg_root_independently()
    {
        var environment = new FakeEnvironmentProvider(
            "/home/test",
            new Dictionary<string, string>
            {
                ["XDG_CONFIG_HOME"] = "/custom/config",
                ["XDG_DATA_HOME"] = "/custom/data",
                ["XDG_STATE_HOME"] = "/custom/state",
                ["XDG_CACHE_HOME"] = "/custom/cache",
            });

        var paths = new LinuxPaths(environment, new NoOpFilePermissions(),
            Host(HostOperatingSystem.Linux)).Paths;

        // A user who redirects only one root must get exactly that, which is
        // why the four are resolved separately rather than derived from a
        // single base.
        paths.Config.Should().Be(Path.Combine("/custom/config", "agent-workspace-launcher"));
        paths.State.Should().Be(Path.Combine("/custom/data", "agent-workspace-launcher"));
        paths.Cache.Should().Be(Path.Combine("/custom/cache", "agent-workspace-launcher"));
        paths.Logs.Should().Be(Path.Combine("/custom/state", "agent-workspace-launcher", "logs"));
    }

    [Fact]
    public void Linux_falls_back_to_the_xdg_defaults_when_unset()
    {
        var environment = new FakeEnvironmentProvider("/home/test");

        var paths = new LinuxPaths(environment, new NoOpFilePermissions(),
            Host(HostOperatingSystem.Linux)).Paths;

        paths.Config.Should().Be(Path.Combine("/home/test", ".config", "agent-workspace-launcher"));
        paths.State.Should().Be(
            Path.Combine("/home/test", ".local", "share", "agent-workspace-launcher"));
    }

    [Fact]
    public void Linux_ignores_a_relative_xdg_value()
    {
        // The XDG specification requires relative values to be ignored, and a
        // relative path here would put launcher state wherever the process
        // happened to be started from.
        var environment = new FakeEnvironmentProvider(
            "/home/test",
            new Dictionary<string, string> { ["XDG_CONFIG_HOME"] = "relative/path" });

        var paths = new LinuxPaths(environment, new NoOpFilePermissions(),
            Host(HostOperatingSystem.Linux)).Paths;

        paths.Config.Should().Be(Path.Combine("/home/test", ".config", "agent-workspace-launcher"));
    }

    [Fact]
    public void MacOS_uses_native_library_conventions_by_default()
    {
        var environment = new FakeEnvironmentProvider("/Users/test");

        var paths = new MacOSPaths(environment, new NoOpFilePermissions(),
            Host(HostOperatingSystem.MacOS)).Paths;

        paths.Config.Should().Be(
            Path.Combine("/Users/test", "Library", "Application Support", "AgentWorkspaceLauncher"));

        paths.Logs.Should().Be(
            Path.Combine("/Users/test", "Library", "Logs", "AgentWorkspaceLauncher"));

        // Caches is reclaimable by the system at any time, which is right for
        // cache and per-launch runtime material and wrong for the workspace
        // clone. The clone therefore lives under Application Support.
        paths.Cache.Should().StartWith(Path.Combine("/Users/test", "Library", "Caches"));
        paths.Runtime.Should().StartWith(Path.Combine("/Users/test", "Library", "Caches"));
        paths.WorkspaceClone.Should().StartWith(
            Path.Combine("/Users/test", "Library", "Application Support"));
    }

    [Fact]
    public void MacOS_is_not_treated_as_linux()
    {
        var environment = new FakeEnvironmentProvider("/Users/test");

        var paths = new MacOSPaths(environment, new NoOpFilePermissions(),
            Host(HostOperatingSystem.MacOS)).Paths;

        // Guards the specific failure the addendum calls out: shipping macOS
        // as "Linux with a different home directory".
        paths.Config.Should().NotContain(".config");
        paths.Config.Should().NotContain("agent-workspace-launcher");
    }

    [Fact]
    public void MacOS_switches_to_xdg_only_on_explicit_opt_in()
    {
        var environment = new FakeEnvironmentProvider(
            "/Users/test",
            new Dictionary<string, string>
            {
                [MacOSPaths.XdgOptInVariable] = "1",
                ["XDG_CONFIG_HOME"] = "/Users/test/.config",
            });

        var paths = new MacOSPaths(environment, new NoOpFilePermissions(),
            Host(HostOperatingSystem.MacOS)).Paths;

        paths.Config.Should().Be(Path.Combine("/Users/test/.config", "agent-workspace-launcher"));
    }

    [Fact]
    public void MacOS_ignores_xdg_variables_without_the_opt_in()
    {
        // Merely having XDG_CONFIG_HOME set, which many Mac developers do for
        // other tools, must not silently relocate the launcher.
        var environment = new FakeEnvironmentProvider(
            "/Users/test",
            new Dictionary<string, string> { ["XDG_CONFIG_HOME"] = "/Users/test/.config" });

        var paths = new MacOSPaths(environment, new NoOpFilePermissions(),
            Host(HostOperatingSystem.MacOS)).Paths;

        paths.Config.Should().Contain("Application Support");
    }

    [Theory]
    [InlineData(HostOperatingSystem.Windows)]
    [InlineData(HostOperatingSystem.Linux)]
    [InlineData(HostOperatingSystem.MacOS)]
    public void Every_platform_separates_all_five_roots(HostOperatingSystem os)
    {
        var environment = new FakeEnvironmentProvider(
            "/home/test",
            new Dictionary<string, string>
            {
                ["APPDATA"] = @"C:\Users\test\AppData\Roaming",
                ["LOCALAPPDATA"] = @"C:\Users\test\AppData\Local",
            });

        var permissions = new NoOpFilePermissions();

        var paths = os switch
        {
            HostOperatingSystem.Windows => new WindowsPaths(environment, permissions, Host(os)).Paths,
            HostOperatingSystem.MacOS => new MacOSPaths(environment, permissions, Host(os)).Paths,
            _ => new LinuxPaths(environment, permissions, Host(os)).Paths,
        };

        // Cache and runtime are discardable; config, state and logs are not.
        // Collapsing any of them together is how a cache purge takes the
        // workspace clone with it.
        new[] { paths.Config, paths.State, paths.Cache, paths.Logs, paths.Runtime }
            .Should().OnlyHaveUniqueItems();
    }
}
