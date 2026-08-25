using FluentAssertions;
using Loadout.Tui.Terminal;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// How a project row is laid out across the column it has to fit in.
/// </summary>
/// <remarks>
/// These exist because the rows were being cut by the terminal rather than by
/// the launcher. Joining the parts with two spaces and letting the list clip
/// whatever overhung produced rows like <c>home-servers-build  [! Attention]
/// claud</c> — the state, which is the part worth scanning down a column, lost
/// to the project with the longest name. It was found by photographing a real
/// console window, because the text the launcher produced was correct and the
/// cut happened after it.
/// </remarks>
public sealed class LauncherRowTests
{
    [Fact]
    public void A_row_that_fits_is_left_alone()
    {
        var row = LauncherWindow.Fit(" ", "Alpha", "[+ Ready]", 40);

        row.Should().Contain("Alpha");
        row.Should().EndWith("[+ Ready]");
        row.Should().NotContain("…");
    }

    [Fact]
    public void The_state_sits_against_the_right_edge_so_it_lines_up()
    {
        var wide = LauncherWindow.Fit(" ", "Alpha", "[+ Ready]", 40);
        var narrow = LauncherWindow.Fit(" ", "A-much-longer-name", "[+ Ready]", 40);

        wide.Length.Should().Be(narrow.Length,
            "rows of different names must still align down the column");
    }

    [Fact]
    public void A_name_too_long_for_the_column_is_cut_and_marked()
    {
        var row = LauncherWindow.Fit(
            " ", "TheCodeSaiyan-PowerShell-tcs.intune.package", "[! Attention]", 40);

        row.Length.Should().BeLessThanOrEqualTo(40);
        row.Should().EndWith("[! Attention]", "the state must survive a long name");
        row.Should().Contain("…", "a silent cut reads as the whole name");
        row.Should().Contain("TheCodeSaiyan", "enough must remain to recognise it");
    }

    [Fact]
    public void In_a_column_too_narrow_for_both_the_name_is_what_survives()
    {
        var row = LauncherWindow.Fit(" ", "Alpha", "[! Attention]", 16);

        row.Length.Should().BeLessThanOrEqualTo(16);
        row.Should().Contain("Alpha",
            "a row whose project cannot be identified is of no use at all");
    }

    [Fact]
    public void Before_the_list_has_been_laid_out_nothing_is_cut()
    {
        // Width is zero until Terminal.Gui has laid the list out, and the rows
        // are built once before that happens. Trimming to zero there would
        // empty every row, and the redraw that follows layout would be the
        // only thing that ever put them back.
        var row = LauncherWindow.Fit(" ", "Alpha", "[+ Ready]", 0);

        row.Should().Contain("Alpha");
        row.Should().Contain("[+ Ready]");
    }
}
