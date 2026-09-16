using System.Diagnostics;
using System.Runtime.Versioning;
using FluentAssertions;
using Loadout.Platform.Abstractions;
using Loadout.Platform.Common;
using Loadout.Platform.Windows;
using Xunit;

namespace Loadout.Tests.Platform;

/// <summary>
/// Whether a child the launcher is driving outlives the launcher.
/// </summary>
/// <remarks>
/// <para>
/// It did, once, and it cost real money: a team run whose coordinator was
/// killed left a lead and a verifier running, spending on turns nobody was
/// reading, until somebody noticed them in the process list.
/// </para>
/// <para>
/// These use real processes because nothing else answers the question.
/// Disposing the lifetime stands in for the launcher ending: on Windows it
/// closes the job, which is the event the kernel acts on however the process
/// dies; elsewhere it runs the same code the exit handler runs.
/// </para>
/// </remarks>
public sealed class ChildLifetimeTests
{
    /// <summary>A process that will sit there for a minute unless something stops it.</summary>
    private static Process StartWaiter()
    {
        var start = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe", "/d /c ping -n 60 127.0.0.1")
            : new ProcessStartInfo("/bin/sh", "-c \"sleep 60\"");

        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardOutput = true;

        return Process.Start(start)!;
    }

    /// <summary>A killed process is gone within a moment, not instantly.</summary>
    private static bool Gone(Process child)
    {
        child.WaitForExit(TimeSpan.FromSeconds(20));

        return child.HasExited;
    }

    [Fact]
    public void A_child_the_launcher_adopted_does_not_outlive_it()
    {
        using var child = StartWaiter();

        var lifetime = new TrackedChildLifetime();
        lifetime.Adopt(child.Id);

        child.HasExited.Should().BeFalse("nothing has ended yet");

        lifetime.Dispose();

        Gone(child).Should().BeTrue("the launcher finished, so what it was driving is finished");
    }

    [Fact]
    public void A_child_nobody_adopted_carries_on()
    {
        // The other half of the promise. Only what the launcher is driving is
        // tied to it: an editor somebody opened is meant to outlive the
        // command that opened it, which is why StartDetached adopts nothing.
        using var child = StartWaiter();

        try
        {
            new TrackedChildLifetime().Dispose();

            child.HasExited.Should().BeFalse();
        }
        finally
        {
            child.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    public void A_reused_identifier_is_not_mistaken_for_the_child_that_had_it()
    {
        // Adopting a live process under a start time from before it began is
        // the same state the launcher is in when the number has come round
        // again: the entry says one process, the machine holds another.
        using var stranger = StartWaiter();

        try
        {
            var lifetime = new RemembersAnEarlierProcess();
            lifetime.UnderTheNumber(stranger.Id);

            lifetime.Dispose();

            stranger.HasExited.Should().BeFalse(
                "the start times disagree, so this is somebody else's process wearing the number");
        }
        finally
        {
            stranger.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    public void Adopting_a_child_that_has_already_gone_is_not_an_error()
    {
        using var child = StartWaiter();
        child.Kill(entireProcessTree: true);
        child.WaitForExit(TimeSpan.FromSeconds(20));

        var lifetime = new TrackedChildLifetime();

        lifetime.Adopt(child.Id);
        lifetime.Dispose();
    }

    [Fact]
    public void The_portable_arrangement_says_it_is_not_enforced()
    {
        // Section 5: what a platform cannot do is reported, never quietly
        // skipped. An exit handler cannot survive a termination, and says so.
        var lifetime = new TrackedChildLifetime();

        lifetime.IsEnforced.Should().BeFalse();
        lifetime.Detail.Should().Contain("terminated outright");
    }

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public async Task On_Windows_a_piped_child_is_in_a_job_that_dies_with_the_launcher()
    {
        // Through the launcher, as a run does: the piped path is the one that
        // starts agents, and the one that must not leave any behind.
        var lifetime = new WindowsChildLifetime();

        lifetime.IsEnforced.Should().BeTrue(
            "Windows has a kernel-enforced answer, and a machine that cannot make a job object says why instead");

        var launcher = new ProcessLauncher(lifetime);

        var started = await launcher.StartPipedAsync(
            new ProcessRequest("cmd.exe", ["/d", "/c", "ping", "-n", "60", "127.0.0.1"]));

        started.Succeeded.Should().BeTrue(started.Error);

        await using var piped = started.Value!;
        using var child = Process.GetProcessById(piped.ProcessId);

        lifetime.Dispose();

        Gone(child).Should().BeTrue("the job closed, and the kernel takes everything inside it");
    }

    /// <summary>
    /// Stands in for a recycled identifier by remembering a start time the
    /// process never had. There is no way to make Windows hand back a
    /// particular number on demand, and the code under test compares exactly
    /// these two values.
    /// </summary>
    private sealed class RemembersAnEarlierProcess : TrackedChildLifetime
    {
        public void UnderTheNumber(int processId) => Remember(processId, DateTime.Now.AddHours(-1));
    }
}
