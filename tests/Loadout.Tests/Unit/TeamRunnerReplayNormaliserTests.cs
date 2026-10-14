using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The replay's normaliser, on its own: what it takes out, and that it takes
/// out nothing else.
/// </summary>
[Trait("Category", "Replay")]
public sealed class TeamRunnerReplayNormaliserTests
{
    private const string Run = "20300101-0000-5a8d";
    private const string WindowsRoot = @"C:\Temp\loadout-run-0f";

    [Fact]
    public void The_run_id_becomes_a_placeholder_inside_a_branch_name_too()
    {
        ReplayNormaliser.Normalise($"teams-{Run}-implementer", Run, "/tmp/x")
            .Should().Be("teams-<run>-implementer");
    }

    [Fact]
    public void A_raw_root_and_the_rest_of_its_path_get_forward_slashes()
    {
        ReplayNormaliser.Normalise(@"in C:\Temp\loadout-run-0f\state\teams\runs\x. Then", Run, WindowsRoot)
            .Should().Be("in <root>/state/teams/runs/x. Then");
    }

    [Fact]
    public void A_root_as_json_escapes_it_is_found_as_well()
    {
        ReplayNormaliser.Normalise(@"{""path"":""C:\\Temp\\loadout-run-0f\\repo\\a""}", Run, WindowsRoot)
            .Should().Be(@"{""path"":""<root>/repo/a""}");
    }

    [Fact]
    public void A_unix_root_is_replaced_and_the_path_after_it_kept()
    {
        ReplayNormaliser.Normalise(@"{""p"":""/tmp/loadout-run-0f/repo""}", Run, "/tmp/loadout-run-0f")
            .Should().Be(@"{""p"":""<root>/repo""}");
    }

    [Fact]
    public void A_time_from_the_stepping_clock_becomes_its_step()
    {
        var at = SteppingClock.Start.AddMilliseconds(42).ToString("O");

        ReplayNormaliser.Normalise($@"{{""at"":""{at}""}}", Run, "/r")
            .Should().Be(@"{""at"":""<t+42>""}");
    }

    [Fact]
    public void A_time_outside_the_clock_s_window_is_kept_as_written()
    {
        const string line = @"{""at"":""2026-09-16T10:00:00+00:00""}";

        ReplayNormaliser.Normalise(line, Run, "/r").Should().Be(line);
    }

    [Fact]
    public void An_eight_hex_id_becomes_a_placeholder_and_a_commit_does_not()
    {
        ReplayNormaliser.Normalise("gate-190e3c37 at a4f21c9", Run, "/r")
            .Should().Be("gate-<id> at a4f21c9");
    }

    [Fact]
    public void Data_fields_are_left_alone()
    {
        const string line = @"{""kind"":""round.ended"",""data"":{""round"":2,""requests"":0,""quiet"":2}}";

        ReplayNormaliser.Normalise(line, Run, "/r").Should().Be(line);
    }
}
