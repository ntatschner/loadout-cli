using System.Text.Json;
using Loadout.Core.Instructions;
using Loadout.Models.Instructions;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Measuring whether sessions did what a specialist asks for.
/// <para>
/// The launcher could say which specialists a launch was given and nothing at
/// all about whether any of them changed what happened next, so a specialist
/// read by nobody cost its tokens on every launch and looked exactly like one
/// that worked.
/// </para>
/// <para>
/// Most of what is asserted here is about the denominator, because that is
/// where a rate like this goes wrong. Counting a session that never had the
/// opportunity measures what the work happened to be rather than what the
/// session did about it — the first version of this reported a fifth of the
/// real rate for exactly that reason.
/// </para>
/// </summary>
public sealed class ProbeServiceTests : IDisposable
{
    private readonly string _root;
    private readonly ProbeService _probes = new();

    private static readonly DateOnly Long = new(2000, 1, 1);

    public ProbeServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "loadout-probe-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp tree is not worth failing the run over.
        }
    }

    private static SpecialistDocument Specialist(SpecialistProbe? probe) =>
        new(
            "foundation.forward-motion",
            SpecialistKind.Foundation,
            "Forward motion",
            "summary",
            SpecialistActivation.None,
            "body",
            100,
            SpecialistOrigin.BuiltIn,
            string.Empty,
            probe);

    private static readonly SpecialistProbe Backgrounded =
        new("started something in the background", ["Bash"], Argument: "run_in_background");

    /// <summary>One transcript, from lines describing tool calls.</summary>
    private void Transcript(string name, bool sidechain, params (string Tool, object Input)[] calls)
    {
        var lines = calls.Select(call => JsonSerializer.Serialize(new
        {
            timestamp = "2026-09-08T12:00:00Z",
            isSidechain = sidechain,
            message = new
            {
                content = new[]
                {
                    new { type = "tool_use", name = call.Tool, input = call.Input },
                },
            },
        }));

        File.WriteAllLines(Path.Combine(_root, name + ".jsonl"), lines);
    }

    private async Task<ProbeWeek?> MeasureAsync(SpecialistProbe probe)
    {
        var measured = await _probes.MeasureAsync(Specialist(probe), [_root], Long);

        measured.Succeeded.Should().BeTrue(measured.Error);

        return measured.Value!.Weeks.SingleOrDefault();
    }

    [Fact]
    public async Task A_session_that_did_it_is_counted_as_having_done_it()
    {
        Transcript("did", false, ("Bash", new { command = "dotnet build", run_in_background = true }));

        var week = await MeasureAsync(Backgrounded);

        week!.Sessions.Should().Be(1);
        week.Showed.Should().Be(1);
    }

    [Fact]
    public async Task A_session_that_could_have_and_did_not_is_counted_against_it()
    {
        Transcript("could", false, ("Bash", new { command = "dotnet build" }));

        var week = await MeasureAsync(Backgrounded);

        // It ran a command and chose not to background it, which is the case
        // the rate exists to measure.
        week!.Sessions.Should().Be(1);
        week.Showed.Should().Be(0);
    }

    [Fact]
    public async Task A_session_that_never_had_the_chance_is_not_counted_at_all()
    {
        Transcript("reading", false, ("Read", new { file_path = "/somewhere" }));

        // No shell command, so no opportunity to background one. Counting this
        // as a failure would mean the number moved with how much of the week
        // happened to be reading rather than with anything the guidance did —
        // and it did: the first version of this reported 23% where the sessions
        // themselves showed 80%.
        (await MeasureAsync(Backgrounded)).Should().BeNull();
    }

    [Fact]
    public async Task A_sub_agents_transcript_is_not_a_session()
    {
        Transcript("errand", true, ("Bash", new { command = "grep -r thing" }));

        // A sub-agent is one errand inside somebody else's session, with a
        // narrow brief and usually no reason to background anything. Sixty-five
        // of them on one machine outnumbered the real sessions two to one in
        // some weeks.
        (await MeasureAsync(Backgrounded)).Should().BeNull();
    }

    [Fact]
    public async Task A_pattern_reads_what_the_call_was_given()
    {
        Transcript("tested", false, ("Bash", new { command = "dotnet test tests/Some.csproj" }));
        Transcript("built", false, ("Bash", new { command = "dotnet build" }));

        var probe = new SpecialistProbe("ran the tests", ["Bash"], Pattern: @"\bdotnet test\b");

        var week = await MeasureAsync(probe);

        week!.Sessions.Should().Be(2);
        week.Showed.Should().Be(1);
    }

    [Fact]
    public async Task A_specialist_with_no_probe_is_refused_rather_than_scored_zero()
    {
        var measured = await _probes.MeasureAsync(Specialist(probe: null), [_root], Long);

        // Nought out of nought would read as a specialist nobody follows. Most
        // specialists have no signature that can honestly be written, and that
        // is a different statement from one that is never followed.
        measured.Failed.Should().BeTrue();
        measured.Error.Should().Contain("no probe");
    }

    [Fact]
    public async Task An_unreadable_line_costs_that_line_and_not_the_session()
    {
        var path = Path.Combine(_root, "damaged.jsonl");

        File.WriteAllLines(path, [
            "{ not json at all",
            JsonSerializer.Serialize(new
            {
                timestamp = "2026-09-08T12:00:00Z",
                isSidechain = false,
                message = new
                {
                    content = new[]
                    {
                        new
                        {
                            type = "tool_use",
                            name = "Bash",
                            input = new { command = "x", run_in_background = true },
                        },
                    },
                },
            }),
        ]);

        var week = await MeasureAsync(Backgrounded);

        week!.Showed.Should().Be(1, "neither transcript format is a published contract");
    }
}
