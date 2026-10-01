using FluentAssertions;
using Loadout.Core.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Who draws each node, and a name that fits them.
/// </summary>
/// <remarks>
/// <para>
/// An office kit can carry a cast: real people, each a woman, a man or neither.
/// The server picks which of them draws each node, so that the name on the desk
/// can be picked to fit the face behind it - "Theo" over an older woman reads as
/// a mistake. The page draws whoever it is told.
/// </para>
/// <para>
/// What has to hold: the lead is the lead; nobody in a run is drawn twice until
/// everybody has been; the same node is the same person every time; and a name
/// is never one for somebody else's gender. With no cast, names are exactly what
/// they were, so nobody already named is renamed.
/// </para>
/// </remarks>
public sealed class OfficeCastTests
{
    private static readonly string[] Workers = ["w0", "w1", "w2", "w3", "w4", "w5", "w6", "w7", "w8"];

    private static OfficeKit CastKit()
    {
        var sheets = new Dictionary<string, OfficeSheet>(StringComparer.Ordinal);

        foreach (var name in Workers.Append("lead").Append("porter"))
        {
            sheets[name] = new OfficeSheet(name + ".png", [96, 96], [48, 80], new Dictionary<string, OfficeAnimation>(),
                name == "lead" ? "woman" : name == "porter" ? "man" : null);
        }

        return OfficeKit.Kit() with
        {
            Sheets = sheets,
            Skins = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["project-lead"] = ["lead"],
                ["keeper"] = ["porter"],
                ["worker"] = Workers,
            },
        };
    }

    private static IEnumerable<(string Node, string Role)> Run(int others)
    {
        yield return ("lead", "role.project-lead");

        for (var i = 0; i < others; i++)
        {
            yield return ($"implementer/{i + 1}", i % 2 == 0 ? "role.implementer" : "role.reviewer");
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(9)]
    public void Nobody_in_a_run_is_drawn_twice_while_there_is_somebody_left(int others)
    {
        foreach (var run in new[] { "20261001-0900-a1b2", "20260930-1216-be60", "x" })
        {
            var cast = OfficeCast.For(CastKit(), run, Run(others));

            cast["lead"].Should().Be("lead");
            cast.Where(one => one.Key != "lead").Select(one => one.Value).Should().OnlyHaveUniqueItems()
                .And.OnlyContain(name => Workers.Contains(name), "two roles with no list of their own share the workers");
        }
    }

    [Fact]
    public void A_run_bigger_than_the_cast_shares_the_cast_out_evenly()
    {
        var counts = OfficeCast.For(CastKit(), "20261001-0900-a1b2", Run(20))
            .Where(one => one.Key != "lead")
            .GroupBy(one => one.Value)
            .Select(group => group.Count())
            .ToList();

        counts.Should().HaveCount(Workers.Length);
        (counts.Max() - counts.Min()).Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public void The_same_node_is_drawn_by_the_same_person_every_time()
    {
        // Worked out from the run and its nodes, not stored, like the names.
        OfficeCast.For(CastKit(), "20261001-0900-a1b2", Run(6))
            .Should().BeEquivalentTo(OfficeCast.For(CastKit(), "20261001-0900-a1b2", Run(6).Reverse()));
    }

    [Fact]
    public void Two_runs_do_not_start_with_the_same_people()
    {
        var firsts = new[] { "20261001-0900-a1b2", "20260930-1216-be60", "20260929-1500-0000", "20260928-0800-1111" }
            .Select(run => OfficeCast.For(CastKit(), run, Run(1))["implementer/1"])
            .Distinct();

        firsts.Should().HaveCountGreaterThan(1);
    }

    [Fact]
    public void A_role_with_its_own_list_is_drawn_from_it()
    {
        OfficeCast.For(CastKit(), "r", [("keeper", "role.keeper")])["keeper"].Should().Be("porter");
    }

    [Fact]
    public void A_kit_with_no_cast_draws_nobody_in_particular()
    {
        OfficeCast.For(OfficeKit.Kit(), "r", Run(3)).Should().BeEmpty();
        OfficeCast.GenderOf(OfficeKit.Kit(), "lead").Should().BeNull();
        OfficeCast.GenderOf(CastKit(), "lead").Should().Be("woman");
    }

    [Theory]
    [InlineData("woman", "man")]
    [InlineData("man", "woman")]
    public void A_name_is_never_one_for_somebody_else(string gender, string other)
    {
        var theirs = DeskNames.FirstsFor(gender);
        var notTheirs = DeskNames.FirstsFor(other).Concat(DeskNames.FirstsFor("nonbinary")).ToHashSet();

        for (var i = 0; i < 400; i++)
        {
            var name = DeskNames.For("20261001-0900-a1b2", $"node/{i}", gender);

            theirs.Should().Contain(name);
            notTheirs.Should().NotContain(name);
        }
    }

    [Fact]
    public void Somebody_drawn_as_neither_gets_a_name_that_suits_anybody()
    {
        var neutral = DeskNames.FirstsFor("nonbinary");

        for (var i = 0; i < 200; i++)
        {
            neutral.Should().Contain(DeskNames.For("20261001-0900-a1b2", $"node/{i}", "nonbinary"));
        }

        // Each list is only its own, so the name says plainly who they are.
        DeskNames.FirstsFor("woman").Intersect(DeskNames.FirstsFor("man")).Should().BeEmpty();
        DeskNames.FirstsFor("woman").Intersect(neutral).Should().BeEmpty();
        DeskNames.FirstsFor("man").Intersect(neutral).Should().BeEmpty();
    }

    [Fact]
    public void With_nothing_to_say_who_somebody_is_their_name_is_what_it_always_was()
    {
        // Not renaming anybody already named: no gender, or one the kit made up, is the old list.
        for (var i = 0; i < 50; i++)
        {
            var before = DeskNames.Full("20260919-0900-aaaa", $"node/{i}");

            DeskNames.Full("20260919-0900-aaaa", $"node/{i}", null).Should().Be(before);
            DeskNames.Full("20260919-0900-aaaa", $"node/{i}", "robot").Should().Be(before);
        }

        DeskNames.FirstsFor(null).Should().HaveCount(48);
    }

    [Fact]
    public void Every_name_fits_a_name_plate()
    {
        DeskNames.Genders.SelectMany(DeskNames.FirstsFor).Should().OnlyContain(name => name.Length > 1 && name.Length <= 6);
    }

    [Fact]
    public void A_kit_check_refuses_a_gender_it_cannot_name_for()
    {
        var kit = CastKit();
        var odd = kit with
        {
            Sheets = kit.Sheets!.ToDictionary(one => one.Key, one => one.Key == "w0" ? one.Value with { Gender = "robot" } : one.Value),
        };
        var problems = new List<string>();

        OfficeScenes.SheetProblems(odd.Sheets!, _ => (96 * 8, 96), problems);

        problems.Should().Contain(problem => problem.Contains("sheet 'w0' is drawn as 'robot'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(97, true)]
    [InlineData(0, false)]
    [InlineData(54, false)]
    [InlineData(96, false)]
    public void A_lap_has_to_be_inside_its_frame(int lap, bool refused)
    {
        // Where a desk's top meets somebody sitting: everything above it is drawn
        // again over the desk, so a lap outside the frame draws nothing or all of it.
        var kit = CastKit();
        var sheets = kit.Sheets!.ToDictionary(one => one.Key, one => one.Key == "w0" ? one.Value with { Lap = lap } : one.Value);
        var problems = new List<string>();

        OfficeScenes.SheetProblems(sheets, _ => (96 * 8, 96), problems);

        problems.Any(problem => problem.Contains("sheet 'w0' puts its lap", StringComparison.Ordinal)).Should().Be(refused);
    }

    [Fact]
    public void A_floor_built_from_a_kit_with_a_cast_carries_it_to_the_page()
    {
        var scene = FloorPlanner.Plan(CastKit(), OfficeRules.Default, "run-a", 5).Scene;

        scene.Cast.Should().NotBeNull();
        scene.Cast!["worker"].Should().Equal(Workers);
        scene.Sheets!.Keys.Should().Contain(["lead", "porter", "w0"]);

        // The built-in kit has nobody in particular, and floors built from it say so.
        FloorPlanner.Plan(OfficeKit.Kit(), OfficeRules.Default, "run-a", 5).Scene.Cast.Should().BeNull();
    }

    [Fact]
    public void A_scene_whose_cast_names_a_sheet_it_lacks_is_refused()
    {
        var scene = FloorPlanner.Plan(CastKit(), OfficeRules.Default, "run-a", 5).Scene;
        var broken = scene with
        {
            Cast = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { ["worker"] = ["w0", "ghost"] },
        };

        OfficeScenes.Problems(broken, _ => (96 * 8, 96))
            .Should().Contain(problem => problem.Contains("cast draws 'worker' with sheet 'ghost'", StringComparison.Ordinal));
    }
}
