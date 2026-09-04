using FluentAssertions;
using Loadout.Core.Diagnostics;
using Loadout.Models;
using Loadout.Models.Diagnostics;
using Loadout.Models.Platform;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The doctor report written as one file somebody can send.
/// </summary>
/// <remarks>
/// Everything here follows from the file existing in order to leave this
/// machine. That is what makes it useful and what makes it dangerous, so it is
/// screened before it is written rather than after — a file created and then
/// reported as unsafe is one somebody may already have attached to something.
/// </remarks>
public sealed class DiagnosticBundleTests
{
    private const string AnthropicKeyShape = "sk-ant-api03-abcdefghijklmnopqrstuvwxyz012345";

    private static readonly HostPlatform Host = new(
        HostOperatingSystem.Windows,
        System.Runtime.InteropServices.Architecture.X64,
        "win-x64",
        "THS-DESKTOP02");

    private static readonly DateTimeOffset Taken = new(2026, 2, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_findings_are_written_as_a_table()
    {
        var text = Build(DiagnosticCheck.Ok("Platform", "Windows", "all present")).Value!;

        text.Should().Contain("| Platform | Windows | Info | all present |");
    }

    [Fact]
    public void The_machine_name_is_taken_out_wherever_it_appears()
    {
        // Not by dropping the row that names it. It is its own check and it also
        // turns up inside paths on a machine whose home directory is named after
        // it, so removing one row would leave it in six others.
        var text = Build(
            DiagnosticCheck.Ok("Platform", "Machine", "THS-DESKTOP02"),
            DiagnosticCheck.Ok("Launcher", "State", @"C:\Users\THS-DESKTOP02\AppData")).Value!;

        text.Should().NotContain("THS-DESKTOP02");
        text.Should().Contain("<machine>");
    }

    [Fact]
    public void A_machine_name_too_short_to_substitute_safely_is_left_alone()
    {
        // Substituting a two-letter name would replace fragments of unrelated
        // words and produce a report that reads as nonsense, which is worse than
        // a name nobody needed.
        var host = Host with { MachineName = "PC" };

        var built = DiagnosticBundle.Build(
            new DiagnosticReport([DiagnosticCheck.Ok("Platform", "Machine", "PC")]),
            host,
            "0.15.0",
            Taken);

        built.Value!.Should().NotContain("<machine>");
    }

    [Fact]
    public void A_finding_that_reads_like_a_credential_stops_the_file_being_written()
    {
        // Contributors are added over time. Trusting every one of them to have
        // been careful would put a value into the one file whose purpose is to
        // be sent somewhere.
        var built = Build(DiagnosticCheck.Ok("Environment", "Key", $"resolved to {AnthropicKeyShape}"));

        built.Failed.Should().BeTrue();
        built.ExitCode.Should().Be(ExitCode.PolicyViolation);

        // Named, never quoted — the refusal must not repeat the thing it is
        // refusing to write.
        built.Error.Should().Contain("Anthropic API key");
        built.Error.Should().NotContain(AnthropicKeyShape);
    }

    [Fact]
    public void A_detail_holding_a_newline_does_not_break_the_table()
    {
        // Captured tool output ends up in a detail, and one newline turns the
        // rest of the table into prose.
        var text = Build(DiagnosticCheck.Ok("Git", "Version", "git version 2.4\nfatal: nope")).Value!;

        text.Should().Contain("| Git | Version | Info | git version 2.4 fatal: nope |");
    }

    [Fact]
    public void A_detail_holding_a_pipe_does_not_split_the_row()
    {
        var text = Build(DiagnosticCheck.Ok("Shell", "Command", "a | b")).Value!;

        text.Should().Contain(@"a \| b");
    }

    [Fact]
    public void The_file_name_sorts_by_when_it_was_taken()
    {
        DiagnosticBundle.FileName(Taken).Should().Be("loadout-diagnostics-20260201-090000.md");
    }

    private static Loadout.Models.Results.OperationResult<string> Build(params DiagnosticCheck[] checks) =>
        DiagnosticBundle.Build(new DiagnosticReport(checks), Host, "0.15.0", Taken);
}
