using FluentAssertions;
using Loadout.Core.Starters;
using Loadout.Models.Platform;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The starters, the install table and the progress record behind
/// <c>loadout start</c>.
/// </summary>
public sealed class FirstProjectStarterTests
{
    [Fact]
    public void The_default_starter_is_one_that_exists()
    {
        Starters.Find(Starters.Default).Should().NotBeNull();
    }

    [Theory]
    [InlineData("web-page")]
    [InlineData("WEB-PAGE")]
    [InlineData(" checklist ")]
    [InlineData("own-idea")]
    public void A_starter_is_found_by_id_however_it_is_typed(string id)
    {
        Starters.Find(id).Should().NotBeNull();
    }

    [Fact]
    public void Every_starter_writes_a_readme_named_after_the_project()
    {
        foreach (var starter in Starters.All)
        {
            starter.Files.Should().ContainKey("README.md", starter.Id);
            starter.Files["README.md"]("Holiday plans").Should().StartWith("# Holiday plans", starter.Id);
        }
    }

    [Fact]
    public void A_starter_that_names_a_page_ships_that_page()
    {
        foreach (var starter in Starters.All.Where(s => s.Page is not null))
        {
            starter.Files.Should().ContainKey(starter.Page!, starter.Id);
        }
    }

    [Fact]
    public void A_name_is_escaped_before_it_goes_into_a_page()
    {
        // Typed by a person, so it is text, not markup.
        var page = Starters.Find("web-page")!.Files["index.html"]("<b>Mine</b> & yours");

        page.Should().Contain("&lt;b&gt;Mine&lt;/b&gt; &amp; yours");
        page.Should().NotContain("<b>Mine</b>");
    }

    [Fact]
    public void Every_prerequisite_has_a_command_for_every_operating_system()
    {
        // Also the guard on the table's own initialisation order: an entry
        // built from a list declared below it would get null, and fail here.
        foreach (var prerequisite in Prerequisites.All)
        {
            foreach (var os in Enum.GetValues<HostOperatingSystem>())
            {
                prerequisite.CommandsFor(os).Should().NotBeEmpty($"{prerequisite.Id} on {os}");
            }
        }
    }

    [Fact]
    public void Progress_moves_through_the_steps_in_order()
    {
        var progress = new FirstProjectProgress();

        progress.Next.Should().Be(GuideStep.Create);

        progress.Finish(GuideStep.Create);
        progress.Finish(GuideStep.Protect);

        progress.Next.Should().Be(GuideStep.Plan);

        foreach (var step in Enum.GetValues<GuideStep>())
        {
            progress.Finish(step);
        }

        progress.Next.Should().BeNull();
    }

    [Fact]
    public void Finishing_a_step_twice_records_it_once()
    {
        var progress = new FirstProjectProgress();

        progress.Finish(GuideStep.Create);
        progress.Finish(GuideStep.Create);

        progress.Done.Should().ContainSingle();
    }

    [Fact]
    public void The_planning_brief_names_the_one_file_it_may_write()
    {
        var brief = FirstProjectBriefs.Plan(Starters.Find("web-page")!, "Recipes");

        brief.Should().Contain("Recipes");
        brief.Should().Contain(FirstProjectBriefs.PlanFile);
        brief.Should().Contain("Do not change any files");
    }

    [Fact]
    public void The_building_brief_keeps_the_project_to_a_browser()
    {
        var brief = FirstProjectBriefs.Build(Starters.Find("own-idea")!, "Recipes");

        brief.Should().Contain(FirstProjectBriefs.PlanFile);
        brief.Should().Contain("Add no packages");
        brief.Should().Contain("index.html", "an idea with no page yet still needs one to open");
    }
}
