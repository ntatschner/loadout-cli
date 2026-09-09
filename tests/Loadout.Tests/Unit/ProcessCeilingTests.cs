using Loadout.Tests.Fakes;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The one limit on how many processes the suite has starting at once.
/// <para>
/// Windows refuses a start with 0xC0000142 when the desktop heap runs out, and
/// the failure lands as a scattering of unrelated tests reporting exit code
/// -1073741502 with no output. It has been diagnosed as a race and as a
/// disturbed binary before the cause was understood, and the fix has now been
/// attempted three times: serialising one collection, then a second cap beside
/// the first, then this.
/// </para>
/// <para>
/// The lesson each attempt taught is the same one: a limit that cannot see
/// every process is not a limit. Two caps of four and two on a four-core runner
/// permitted six, and that run failed forty-three starts. So what is asserted
/// here is the property rather than any one caller's good behaviour — whoever
/// holds places, no more than the ceiling are held at once.
/// </para>
/// </summary>
public sealed class ProcessCeilingTests
{
    [Fact]
    public async Task No_more_than_the_ceiling_are_ever_held_at_once()
    {
        var held = 0;
        var peak = 0;
        var sync = new object();

        // Comfortably more than any ceiling this computes, so the gate is the
        // thing under test rather than the number of callers.
        var callers = Enumerable.Range(0, ThrottledProcessLauncher.Ceiling * 8).Select(async _ =>
        {
            using (await ThrottledProcessLauncher.EnterAsync())
            {
                lock (sync)
                {
                    held++;
                    peak = Math.Max(peak, held);
                }

                // Long enough that the callers genuinely overlap. Without this
                // each could take and release its place before the next asked,
                // and a broken gate would pass.
                await Task.Delay(15);

                lock (sync)
                {
                    held--;
                }
            }
        });

        await Task.WhenAll(callers);

        peak.Should().BeLessThanOrEqualTo(
            ThrottledProcessLauncher.Ceiling,
            "the gate is the only thing standing between the suite and a host that "
            + "refuses to start any more processes");

        // And it is a gate rather than a lock: holding the suite to one process
        // at a time would fix this by making the run several minutes longer.
        peak.Should().BeGreaterThan(1, "the ceiling is meant to permit real concurrency");
    }

    [Fact]
    public async Task A_start_windows_refused_is_tried_again()
    {
        // 'cmd /c exit 3221225794' returns 0xC0000142 and writes nothing, which
        // is indistinguishable from the real refusal — the same exit code and
        // the same silence on both streams. So the retry sees what it would see
        // on a machine that is short of desktop heap, and exhausts its attempts
        // rather than passing the code back on the first one.
        var launcher = new ThrottledProcessLauncher();

        var started = DateTimeOffset.UtcNow;

        var result = await launcher.RunAsync(
            new Loadout.Platform.Abstractions.ProcessRequest(
                OperatingSystem.IsWindows() ? "cmd" : "sh",
                OperatingSystem.IsWindows()
                    ? ["/c", "exit 3221225794"]
                    : ["-c", "exit 0"],
                Directory.GetCurrentDirectory()),
            TimeSpan.FromSeconds(30));

        if (!OperatingSystem.IsWindows())
        {
            // The status code is Windows'. Elsewhere there is nothing to
            // reproduce, and asserting on a fabricated one would test the test.
            result.Succeeded.Should().BeTrue();

            return;
        }

        // Still fails in the end — a refusal that never stops being a refusal is
        // a failure, and swallowing it would hide a machine that needs looking
        // at. What the retry buys is surviving a shortage that passes.
        result.Value!.ExitCode.Should().Be(unchecked((int)0xC0000142));

        // And it did wait: five back-offs of half a second upwards is over seven
        // seconds, so returning promptly would mean it never retried at all.
        (DateTimeOffset.UtcNow - started).Should().BeGreaterThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void The_ceiling_is_scaled_to_the_machine_and_never_below_two() =>
        // The limit is the host's desktop heap, not a property of the suite, so
        // a small runner has to be held tighter than a workstation. Two is the
        // floor because one would be a serialisation.
        ThrottledProcessLauncher.Ceiling
            .Should().Be(Math.Max(2, Environment.ProcessorCount / 2))
            .And.BeGreaterThanOrEqualTo(2);
}
