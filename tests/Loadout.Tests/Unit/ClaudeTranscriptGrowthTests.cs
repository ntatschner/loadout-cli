using FluentAssertions;
using Loadout.Core.Sessions;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// A live session's transcript grows with every turn, and the history reads
/// its first lines again only when the file is not simply the same with more
/// on the end.
/// </summary>
/// <remarks>
/// Whether the head was read again is shown by changing a line inside it, far
/// enough in that the start of the file is the same and far enough from the
/// end that the read of the end cannot see it: a history that parsed the head
/// again sees the changed title, one that took it from the last reading sees
/// the old one.
/// </remarks>
public sealed class ClaudeTranscriptGrowthTests : IDisposable
{
    private readonly TemporaryWorkspace _space = new();
    private readonly string _directory;
    private readonly string _path;

    public ClaudeTranscriptGrowthTests()
    {
        _directory = _space.Scratch("code");

        var folder = Path.Combine(_space.Scratch("home"), ".claude", "projects", "code");
        Directory.CreateDirectory(folder);
        _path = Path.Combine(folder, "2b7c1d64-0000-4000-8000-0000000000aa.jsonl");
    }

    public void Dispose() => _space.Dispose();

    private ClaudeSessionHistory History(bool kept = false) =>
        new(new FakeEnvironmentProvider(_space.Scratch("home")), kept ? _space.Paths : null);

    /// <summary>
    /// Four hundred and fifty lines of about a kilobyte, so the head stops
    /// short of the end and the end is read for the title. The title sits at
    /// line two hundred: past the start, and well before the last 128 KB.
    /// </summary>
    private List<string> Transcript(string headTitle, string? directory = null)
    {
        var lines = new List<string>
        {
            Json(new { type = "user", cwd = directory ?? _directory, gitBranch = "work", message = new { role = "user", content = "Opening prompt" } }),
        };

        for (var i = 1; i < 450; i++)
        {
            lines.Add(i == 200
                ? Json(new { type = "ai-title", aiTitle = headTitle })
                : Filler(i));
        }

        return lines;
    }

    private static string Filler(int i) =>
        Json(new { type = "assistant", message = new { role = "assistant", content = $"turn {i:D4} " + new string('x', 1000) } });

    private static string Json(object value) => System.Text.Json.JsonSerializer.Serialize(value);

    private void Write(IEnumerable<string> lines) => File.WriteAllLines(_path, lines);

    private async Task<AgentSession> OnlyAsync(ClaudeSessionHistory history)
    {
        var sessions = await history.ListAsync(10);

        sessions.Succeeded.Should().BeTrue();

        return sessions.Value!.Single();
    }

    [Fact]
    public async Task A_transcript_that_grew_keeps_what_its_head_said()
    {
        Write(Transcript("Head title"));
        (await OnlyAsync(History())).Title.Should().Be("Head title");

        var grown = Transcript("Edit title");
        grown.Add(Filler(450));
        Write(grown);

        (await OnlyAsync(History())).Title.Should().Be("Head title", "the start is unchanged and the file only longer, so the head is not parsed again");
    }

    [Fact]
    public async Task A_title_added_at_the_end_of_a_grown_transcript_is_the_one_shown()
    {
        Write(Transcript("Head title"));
        await OnlyAsync(History());

        var grown = Transcript("Head title");
        grown.Add(Json(new { type = "ai-title", aiTitle = "Renamed later" }));
        Write(grown);

        var session = await OnlyAsync(History());

        session.Title.Should().Be("Renamed later");
        session.Directory.Should().Be(_directory);
        session.Branch.Should().Be("work");
    }

    [Fact]
    public async Task A_transcript_whose_start_changed_is_read_again_in_full()
    {
        Write(Transcript("Head title"));
        await OnlyAsync(History());

        var elsewhere = _space.Scratch("other");
        var rewritten = Transcript("Edit title", elsewhere);
        rewritten.Add(Filler(450));
        Write(rewritten);

        var session = await OnlyAsync(History());

        session.Title.Should().Be("Edit title");
        session.Directory.Should().Be(elsewhere);
    }

    [Fact]
    public async Task A_transcript_that_got_shorter_is_read_again_in_full()
    {
        Write(Transcript("Head title"));
        await OnlyAsync(History());

        Write(Transcript("Edit title").Take(440));

        (await OnlyAsync(History())).Title.Should().Be("Edit title");
    }

    [Fact]
    public async Task After_a_restart_the_head_comes_from_the_reading_kept_on_disk()
    {
        Write(Transcript("Head title"));
        await OnlyAsync(History(kept: true));

        ClaudeSessionHistory.Forget();

        var grown = Transcript("Edit title");
        grown.Add(Filler(450));
        Write(grown);

        (await OnlyAsync(History(kept: true))).Title.Should().Be("Head title");
    }
}
