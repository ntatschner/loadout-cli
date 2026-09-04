using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Core.Projects;
using Loadout.Core.Usage;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The usage report written for somebody who is not at this terminal.
/// </summary>
/// <remarks>
/// Formatting rather than new arithmetic — every figure here is one the report
/// already held. What the formats have to get right is the two things a table
/// on screen gets for free: that a name holding a comma does not silently
/// become two columns, and that a total nobody knows is incomplete never
/// travels without the caveat attached.
/// </remarks>
public sealed class UsageReportFormatTests
{
    [Fact]
    public void The_markdown_says_what_window_it_covers()
    {
        var lines = UsageCommand.Markdown(Report(), Rows(("starstats", 1000)), "project").ToList();

        lines[0].Should().Contain("2026-02-01");
    }

    [Fact]
    public void An_incomplete_total_carries_its_caveat_above_the_table()
    {
        // In a terminal the caveat sits under the table, where the eye
        // finishes. Pasted into a message it would be below the fold, and a
        // total nobody knows is incomplete is worse than one nobody reads.
        var report = Report(new UsageIntegrity(FilesRead: 3, RecordsUnrecognised: 4));

        var lines = UsageCommand.Markdown(report, Rows(("starstats", 1000)), "project").ToList();

        var caveat = lines.FindIndex(line => line.StartsWith('>'));
        var table = lines.FindIndex(line => line.StartsWith("| By", StringComparison.Ordinal));

        caveat.Should().BeGreaterThan(-1, "the report knows it could not read everything");
        caveat.Should().BeLessThan(table);
    }

    [Fact]
    public void A_complete_total_carries_no_caveat()
    {
        // A note that is always there is one nobody reads.
        UsageCommand.Markdown(Report(), Rows(("starstats", 1000)), "project")
            .Should().NotContain(line => line.StartsWith('>'));
    }

    [Fact]
    public void An_empty_window_says_so_rather_than_printing_an_empty_table()
    {
        UsageCommand.Markdown(Report(), [], "project")
            .Should().Contain("Nothing was recorded in this window.");
    }

    [Fact]
    public void The_csv_names_its_columns_after_what_it_was_grouped_by()
    {
        UsageCommand.Csv(Report(), Rows(("starstats", 1000)), "day")
            .First().Should().StartWith("day,registered,");
    }

    [Fact]
    public void A_name_holding_a_comma_stays_one_column()
    {
        // A project name is somebody's own text and a directory path can hold a
        // comma. Written raw it parses into the wrong number of columns, which
        // is worse than failing.
        var lines = UsageCommand.Csv(Report(), Rows(("star, stats", 1000)), "project").ToList();

        lines[1].Should().StartWith("\"star, stats\",");
    }

    [Fact]
    public void A_name_holding_a_quote_has_it_doubled_rather_than_dropped()
    {
        var lines = UsageCommand.Csv(Report(), Rows(("the \"good\" one", 1000)), "project").ToList();

        lines[1].Should().StartWith("\"the \"\"good\"\" one\",");
    }

    [Fact]
    public void An_ordinary_name_is_not_quoted()
    {
        UsageCommand.Csv(Report(), Rows(("starstats", 1000)), "project")
            .Skip(1).First().Should().StartWith("starstats,");
    }

    private static UsageReport Report(UsageIntegrity? integrity = null) =>
        new(
            new DateOnly(2026, 2, 1),
            new UsageTotals(1000, 0, 0, 200, 300, 50),
            [],
            [],
            [],
            [],
            integrity ?? new UsageIntegrity(FilesRead: 3, RecordsCounted: 10));

    private static IReadOnlyList<UsageGroup> Rows(params (string Name, long Total)[] rows) =>
        rows
            .Select(row => new UsageGroup(
                row.Name,
                new UsageTotals(row.Total, 0, 0, row.Total / 2, 100, 0),
                IsRegistered: true))
            .ToList();

    private static IReadOnlyList<UsageGroup> Unregistered(string name) =>
        [new UsageGroup(name, new UsageTotals(1000, 0, 0, 500, 100, 0), IsRegistered: false)];

    [Fact]
    public void A_directory_that_is_not_a_project_is_marked_in_the_markdown()
    {
        var lines = UsageCommand
            .Markdown(Report(), Unregistered("Roblox_Cat_Game"), "project")
            .ToList();

        // This is the format somebody sends to a colleague. A directory an
        // agent happened to work in, sitting under a column headed "project",
        // is a claim the sender did not mean to make — and it was reported as
        // exactly that: usage "picking up repos not registered as projects".
        lines.Should().Contain(line => line.Contains("Roblox_Cat_Game ?", StringComparison.Ordinal));
        lines.Should().Contain(line => line.Contains("not a registered project", StringComparison.Ordinal));
    }

    [Fact]
    public void A_registered_project_carries_no_marker()
    {
        var lines = UsageCommand.Markdown(Report(), Rows(("starstats", 1000)), "project").ToList();

        lines.Should().NotContain(line => line.Contains("starstats ?", StringComparison.Ordinal));
        lines.Should().NotContain(line => line.Contains("not a registered project", StringComparison.Ordinal));
    }

    [Fact]
    public void The_csv_says_which_are_registered_in_a_column_of_its_own()
    {
        // The one format that already got this right, and the reason the
        // others needed fixing rather than the rule needing inventing.
        UsageCommand.Csv(Report(), Unregistered("Roblox_Cat_Game"), "project")
            .Skip(1).First().Should().Contain(",no");
    }
}
