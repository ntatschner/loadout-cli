using FluentAssertions;
using Loadout.Platform.Abstractions;
using Loadout.Platform.Common;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// Withholding variables from a child process.
/// </summary>
/// <remarks>
/// Opening an editor from a terminal that editor owns hands the new instance a
/// copy of the old one's private environment. VS Code sets
/// ELECTRON_RUN_AS_NODE=1 for its command line shim, so the copy made the
/// editor we started run as Node: it read the folder it was asked to open as a
/// module path, said "Cannot find module", and put up a blank window with no
/// workbench in it.
/// </remarks>
public sealed class ProcessEnvironmentTests : IDisposable
{
    private const string Marker = "VSCODE_LOADOUT_TEST_MARKER";

    public ProcessEnvironmentTests() =>
        Environment.SetEnvironmentVariable(Marker, "poison");

    public void Dispose() =>
        Environment.SetEnvironmentVariable(Marker, null);

    /// <summary>Echoes one variable, in whatever shell this platform has.</summary>
    private static ProcessRequest Echo(IReadOnlyList<string>? remove) =>
        OperatingSystem.IsWindows()
            ? new ProcessRequest(
                "cmd.exe",
                ["/c", $"echo %{Marker}%"],
                RemoveEnvironmentPrefixes: remove)
            : new ProcessRequest(
                "/bin/sh",
                ["-c", $"echo ${Marker}"],
                RemoveEnvironmentPrefixes: remove);

    [Fact]
    public async Task A_child_inherits_the_environment_by_default()
    {
        var result = await new ProcessLauncher().RunAsync(Echo(remove: null));

        // The control. Without it, the test below could pass because the child
        // never ran rather than because the variable was withheld.
        result.Succeeded.Should().BeTrue(result.Error ?? string.Empty);
        result.Value!.StandardOutput.Should().Contain("poison");
    }

    [Fact]
    public async Task A_withheld_prefix_does_not_reach_the_child()
    {
        var result = await new ProcessLauncher().RunAsync(Echo(remove: ["VSCODE_"]));

        result.Succeeded.Should().BeTrue(result.Error ?? string.Empty);
        result.Value!.StandardOutput.Should().NotContain("poison");
    }

    [Fact]
    public async Task Something_set_explicitly_survives_its_own_prefix_being_withheld()
    {
        var request = OperatingSystem.IsWindows()
            ? new ProcessRequest(
                "cmd.exe",
                ["/c", $"echo %{Marker}%"],
                Environment: new Dictionary<string, string> { [Marker] = "kept" },
                RemoveEnvironmentPrefixes: ["VSCODE_"])
            : new ProcessRequest(
                "/bin/sh",
                ["-c", $"echo ${Marker}"],
                Environment: new Dictionary<string, string> { [Marker] = "kept" },
                RemoveEnvironmentPrefixes: ["VSCODE_"]);

        var result = await new ProcessLauncher().RunAsync(request);

        // Asking for a prefix to be withheld and then setting one variable under
        // it means the one that was set. Removing it anyway would be a trap.
        result.Value!.StandardOutput.Should().Contain("kept");
    }
}
