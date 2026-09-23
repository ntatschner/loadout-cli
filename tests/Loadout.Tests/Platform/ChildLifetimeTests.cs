using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
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

    [Fact]
    public void Stopping_nothing_at_exit_names_no_assembly_that_may_not_be_loadable()
    {
        // `loadout update` replaces the running executable, and a
        // self-contained single-file build reads its assemblies back out of
        // the bundle at its own path: after the swap, anything not already
        // loaded cannot be loaded at all. The exit handler ran straight into
        // that, because the JIT resolves the types a method names before it
        // runs a line of it, and StopAll named System.Diagnostics.Process
        // even with no children to stop. Every update ended in an unhandled
        // FileNotFoundException over work that had succeeded.
        const string Fragile = "System.Diagnostics.Process";

        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var stopAll = typeof(TrackedChildLifetime).GetMethod("StopAll", flags)!;
        var stop = typeof(TrackedChildLifetime).GetMethod("Stop", flags)!;

        // The instrument first: a scanner that cannot see the assembly
        // anywhere would pass this whatever StopAll did. Stop is where the
        // Process call went, so it is the case whose answer is known.
        AssembliesNamedBy(stop).Should().Contain(
            Fragile, "this is the method the call was moved into");

        AssembliesNamedBy(stopAll).Should().NotContain(
            Fragile,
            "an exit with no children to stop must not have to load an assembly, "
            + "because after an update there is nowhere left to load it from");
    }

    [Fact]
    public void A_child_that_cannot_be_stopped_does_not_take_the_shutdown_with_it()
    {
        // ProcessExit has no frame above it: anything thrown there is
        // unhandled by definition, and turns a finished command into a crash
        // report. Stopping a child is not worth that.
        var lifetime = new RefusesToStop();
        lifetime.UnderTheNumber(Environment.ProcessId);

        var thrown = Record.Exception(lifetime.StopAllQuietly);

        thrown.Should().BeNull();
        lifetime.Tried.Should().BeTrue("the guard must swallow the failure, not skip the work");
    }

    /// <summary>
    /// The assemblies a method's own body names, read from its IL.
    /// </summary>
    /// <remarks>
    /// Its body rather than its call tree: the question is what the JIT has
    /// to resolve to compile this one method, which is exactly the tokens it
    /// carries.
    /// </remarks>
    private static IReadOnlyCollection<string> AssembliesNamedBy(MethodBase method)
    {
        var module = method.Module;
        var named = new HashSet<string>(StringComparer.Ordinal);

        foreach (var token in TokensIn(method))
        {
            try
            {
                var member = module.ResolveMember(token);
                var owner = member as Type ?? member?.DeclaringType;

                if (owner?.Assembly.GetName().Name is { } name)
                {
                    named.Add(name);
                }
            }
            catch (ArgumentException)
            {
                // A token this simple walk cannot resolve on its own, such as
                // one needing a generic context. Nothing here depends on it.
            }
        }

        return named;
    }

    /// <summary>The metadata tokens a method body carries, walked opcode by opcode.</summary>
    private static IEnumerable<int> TokensIn(MethodBase method)
    {
        var il = method.GetMethodBody()?.GetILAsByteArray() ?? [];
        var known = typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(code => (ushort)code.Value);

        var at = 0;

        while (at < il.Length)
        {
            var key = (ushort)il[at];

            if (key == 0xFE)
            {
                key = (ushort)(0xFE00 | il[at + 1]);
                at += 2;
            }
            else
            {
                at += 1;
            }

            if (!known.TryGetValue(key, out var code))
            {
                // An opcode this walk does not know means the rest of the
                // stream cannot be trusted, so stop rather than read noise.
                yield break;
            }

            var operand = code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI
                    or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, at)),
                _ => 4,
            };

            if (code.OperandType is OperandType.InlineMethod or OperandType.InlineField
                or OperandType.InlineType or OperandType.InlineTok)
            {
                yield return BitConverter.ToInt32(il, at);
            }

            at += operand;
        }
    }

    /// <summary>
    /// A lifetime whose stopping fails, standing in for anything that can go
    /// wrong on the way out - a load that cannot be satisfied after an
    /// update, most of all.
    /// </summary>
    private sealed class RefusesToStop : TrackedChildLifetime
    {
        public bool Tried { get; private set; }

        public void UnderTheNumber(int processId) => Remember(processId, DateTime.Now);

        protected override void Stop(int processId, DateTime startedAt)
        {
            Tried = true;

            throw new FileNotFoundException("the assembly is no longer where it was");
        }
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
