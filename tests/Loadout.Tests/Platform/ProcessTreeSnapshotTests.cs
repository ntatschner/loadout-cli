using System.Diagnostics;
using FluentAssertions;
using Loadout.Platform;
using Loadout.Platform.Abstractions;
using Loadout.Platform.Common;
using Xunit;

namespace Loadout.Tests.Platform;

/// <summary>
/// The real snapshot on whichever platform this runs: it finds this process
/// with memory, and a child this test starts under it.
/// </summary>
public sealed class ProcessTreeSnapshotTests
{
    [Fact]
    public async Task A_child_this_process_started_is_counted_under_it()
    {
        using var child = Process.Start(OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe", "/c ping -n 30 127.0.0.1 > nul") { CreateNoWindow = true, UseShellExecute = false }
            : new ProcessStartInfo("sleep", "30") { UseShellExecute = false })!;

        try
        {
            var tree = PlatformServices.CreateProcessTree(new ProcessLauncher());
            var table = await tree.SnapshotAsync();
            var self = Environment.ProcessId;

            table.Should().Contain(entry => entry.Id == self && entry.ResidentBytes > 0, "this process is running and holds memory");
            table.Should().Contain(entry => entry.Id == child.Id && entry.Parent == self, "the child names this process as its parent");

            var mine = table.Single(entry => entry.Id == self).ResidentBytes!.Value;
            var withChild = ProcessTrees.Resident(table, [self])[self];

            withChild.Should().BeGreaterThan(mine, "the child's memory is counted with this process's");
        }
        finally
        {
            child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync();
        }
    }
}
