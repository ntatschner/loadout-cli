using FluentAssertions;
using Loadout.Core.Tools;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>Whether a new tool is one the catalogue already has.</summary>
public sealed class ToolOverlapTests
{
    private const string Clear =
        "param([string]$CachePath, [int]$OlderThanDays = 7)\n"
        + "$cutoff = (Get-Date).AddDays(-$OlderThanDays)\n"
        + "Get-ChildItem -Path $CachePath -Recurse -File | Where-Object { $_.LastWriteTime -lt $cutoff } | Remove-Item -Force\n"
        + "Write-Output 'freed'\n";

    [Fact]
    public void Identical_scripts_are_duplicates()
    {
        var a = new ToolShape(["cache"], "Clears a cache.", Clear);
        var b = new ToolShape(["disk"], "Something else entirely.", "# a comment\n" + Clear.Replace("\n", "\r\n   ", StringComparison.Ordinal));

        var score = ToolOverlap.Score(a, b);

        score.Duplicate.Should().BeTrue();
        score.Overlaps.Should().BeTrue();
    }

    [Fact]
    public void Threshold_flags_overlap()
    {
        var a = new ToolShape(["cache", "disk", "cleanup"], "Clears old files from a cache directory.", Clear);
        var b = new ToolShape(
            ["cache", "disk", "cleanup"],
            "Clears old files from a cache directory.",
            Clear.Replace("Write-Output 'freed'", "Write-Output 'done'", StringComparison.Ordinal));

        var score = ToolOverlap.Score(a, b);

        score.Duplicate.Should().BeFalse();
        score.Score.Should().BeGreaterThanOrEqualTo(ToolOverlap.Threshold);
        score.Overlaps.Should().BeTrue();
    }

    [Fact]
    public void Unrelated_tools_do_not()
    {
        var a = new ToolShape(["cache", "disk"], "Clears old files from a cache directory.", Clear);
        var b = new ToolShape(
            ["service", "restart"],
            "Restarts a stopped service and waits for it.",
            "param([string]$Name)\nRestart-Service -Name $Name\nStart-Sleep -Seconds 5\nGet-Service $Name\n");

        ToolOverlap.Score(a, b).Overlaps.Should().BeFalse();
    }
}
