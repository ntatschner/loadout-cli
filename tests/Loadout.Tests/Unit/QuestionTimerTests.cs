using FluentAssertions;
using Loadout.Agents.Teams;
using Loadout.Cli.Commands;
using Loadout.Core.Teams;
using Loadout.Models.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// That a question the run will answer itself says when, and how, where the
/// page and the launcher read it.
/// </summary>
/// <remarks>
/// The page counted down to the run giving up and said "then the run stops"
/// over a question the timer was about to answer, which is the opposite of
/// what happened.
/// </remarks>
public sealed class QuestionTimerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "loadout-timer-" + Guid.NewGuid().ToString("N"));

    public QuestionTimerTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static readonly ReportQuestion Question = new("Proceed with one implementer?", ["yes", "no"], "yes");

    /// <remarks>
    /// Through the wrapper every run puts round its console, because a timed
    /// overload the wrapper did not forward would fall to the interface's
    /// default and ask the plain question - the trap this codebase has fallen
    /// into twice with the wrapper's other methods.
    /// </remarks>
    [Fact]
    public async Task A_timed_question_is_written_with_when_and_how_the_run_will_answer_it()
    {
        var console = new DashboardTeamConsole(TimeProvider.System, _ => { });

        console.Starting(_directory);

        using var one = new OneAtATime(console);

        var at = DateTimeOffset.UtcNow.AddMinutes(30);
        var asking = one.DecideAsync(Question, new QuestionTimer(at, QuestionTimer.SendsBack));

        var pending = await WrittenAsync();

        pending.Timer.Should().Be(new QuestionTimer(at, QuestionTimer.SendsBack));
        pending.Until.Should().BeAfter(at, "the run gives up later than its timer answers");

        await NodePermissions.AnswerAsync(_directory, pending.Id, new AskAnswer(true, "yes", Chosen: "yes"));

        (await asking).Should().Be("yes");
    }

    [Fact]
    public async Task An_untimed_question_carries_no_timer()
    {
        var console = new DashboardTeamConsole(TimeProvider.System, _ => { });

        console.Starting(_directory);

        var asking = console.DecideAsync(Question);
        var pending = await WrittenAsync();

        pending.Timer.Should().BeNull();

        await NodePermissions.AnswerAsync(_directory, pending.Id, new AskAnswer(true, "no", Chosen: "no"));
        (await asking).Should().Be("no");
    }

    private async Task<PendingAsk> WrittenAsync()
    {
        using var give = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        while (true)
        {
            if (NodePermissions.Pending(_directory).FirstOrDefault() is { } pending)
            {
                return pending;
            }

            await Task.Delay(20, give.Token);
        }
    }
}
