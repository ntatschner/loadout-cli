using FluentAssertions;
using Loadout.Core.Configuration;
using Loadout.Core.Teams;
using Loadout.Models.Configuration;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The office's size range, as the setting is written and as the page is given it.
/// </summary>
public sealed class OfficeScaleTests
{
    [Theory]
    [InlineData("1-2", 1, 2)]
    [InlineData(" 1.5-3 ", 1.5, 3)]
    [InlineData("0.5-8", 0.5, 8)]
    [InlineData("2-2", 2, 2)]
    public void A_range_reads_as_its_two_ends(string text, double min, double max)
    {
        OfficeScale.Parse(text).Should().Be(new OfficeScale(min, max));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2")]
    [InlineData("3-1")]
    [InlineData("0.4-2")]
    [InlineData("1-8.5")]
    [InlineData("1-2-3")]
    [InlineData("one-two")]
    public void Anything_else_is_not_a_range(string? text)
    {
        OfficeScale.Parse(text).Should().BeNull();
    }

    [Fact]
    public void A_range_is_written_the_way_it_is_read_whatever_the_culture()
    {
        var was = Thread.CurrentThread.CurrentCulture;

        try
        {
            // A comma for the decimal point would write a range nothing reads back.
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");

            new OfficeScale(1.5, 2.5).ToString().Should().Be("1.5-2.5");
            OfficeScale.Parse(new OfficeScale(1.5, 2.5).ToString()).Should().Be(new OfficeScale(1.5, 2.5));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = was;
        }
    }

    [Fact]
    public void The_setting_refuses_a_range_the_page_cannot_use_and_keeps_one_it_can()
    {
        var entry = ConfigKeys.Find("team-office-scale")!;
        var machine = new MachineConfig();

        var act = () => entry.Write(new LauncherConfig(), machine, "3-1");

        act.Should().Throw<FormatException>().WithMessage("*3-1*");

        entry.Write(new LauncherConfig(), machine, " 1-3 ");
        machine.Teams.OfficeScale.Should().Be("1-3");

        entry.Write(new LauncherConfig(), machine, "");
        machine.Teams.OfficeScale.Should().BeEmpty("clearing it goes back to the default");
    }
}
