using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// The probe itself, run against a healthy host so its report is known to be
/// right where the answer is known. An instrument that has never been read
/// against a case with a known answer is not an instrument.
/// </summary>
public sealed class SpawnRefusalProbeTests
{
    [BuiltCliFact]
    public void On_a_healthy_host_every_variant_starts_and_the_report_says_so()
    {
        var report = SpawnRefusalProbe.Build(LoadoutProcess.Executable!);

        // Kept beside the results as the healthy baseline, so a refusal's
        // report on the same runner has something to be read against.
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "spawn-probe-healthy.txt"), report);

        report.Should().Contain("== spawn variants");

        // The shell variants exit 7 by construction; the executable answers
        // --version with 0. A refusal would read 0xC0000142 or a thrown
        // exception on the same lines.
        report.Should().Contain($"{"inherited env, redirected",-40}: exit 7 ");
        report.Should().Contain($"{"minimal explicit env, redirected",-40}: exit 7 ");
        report.Should().Contain($"{"inherited env, no window",-40}: exit 7 ");
        report.Should().Contain($"{"inherited env, nothing redirected",-40}: exit 7 ");
        report.Should().Contain($"{"the suite's own executable, --version",-40}: exit 0 ");

        report.Should().Contain("odd entries     : 0");
        report.Should().Contain("exists=True");

        if (OperatingSystem.IsWindows())
        {
            report.Should().Contain("== console of this host");

            // ConsoleIsolation's whole purpose: the toolkit must not believe
            // the host's hidden console is a terminal, or every screen test
            // drives the real driver against it. Without the initialiser
            // this reads attached=True on any Windows host under dotnet test.
            report.Should().Contain("toolkit sees    : attached=False",
                "the screen tests must run the driver degraded, not against the host's console");
        }
    }
}
