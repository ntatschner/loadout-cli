using FluentAssertions;
using Loadout.Core.Tasks;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>The rule a title is kept to, on its own.</summary>
public sealed class TaskTitlesTests
{
    [Theory]
    [InlineData("Fix the login")]
    [InlineData("Exactly seventy-two characters long, which is the limit for a title....")]
    public void A_title_within_the_limit_is_kept(string title)
    {
        TaskTitles.Shortens(title).Should().BeFalse();
        TaskTitles.Shorten(title).Should().Be(title);
    }

    [Fact]
    public void A_title_over_the_limit_never_comes_back_over_it()
    {
        foreach (var length in Enumerable.Range(TaskTitles.Length + 1, 400))
        {
            var words = string.Join(' ', Enumerable.Repeat("word", length)).Substring(0, length);

            TaskTitles.Shorten(words).Length.Should().BeLessThanOrEqualTo(TaskTitles.Length);
            TaskTitles.Shorten(words).Should().EndWith("…");
        }
    }

    [Fact]
    public void One_long_word_is_cut_where_it_has_to_be()
    {
        var unbroken = new string('x', 200);

        TaskTitles.Shorten(unbroken).Should().Be(new string('x', TaskTitles.Length - 1) + "…");
    }

    [Fact]
    public void A_title_over_several_lines_is_put_on_one()
    {
        TaskTitles.Shorten("Fix the login\n\n  on the  website ").Should().Be("Fix the login on the website");
    }

    [Fact]
    public void The_cut_drops_a_dangling_comma()
    {
        var title = "Make the lists scannable in Studio and in the command line interface, then the rest";

        TaskTitles.Shorten(title).Should().Be("Make the lists scannable in Studio and in the command line interface…");
    }

    [Fact]
    public void The_original_goes_first_and_is_not_kept_twice()
    {
        TaskTitles.Keep("the original", string.Empty).Should().Be("the original");
        TaskTitles.Keep("the original", "more").Should().Be("the original\n\nmore");
        TaskTitles.Keep("the original", "the original\n\nmore").Should().Be("the original\n\nmore");
    }
}
