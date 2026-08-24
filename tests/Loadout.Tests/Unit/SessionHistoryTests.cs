using Loadout.Core.Sessions;
using Loadout.Tests.Fakes;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Covers reading each agent's session history off disk.
/// <para>
/// Neither format is a published contract — both are private storage belonging
/// to somebody else's program — so these fixtures are copies of real files, and
/// the tests care most about what happens when a file is not what was expected.
/// A history that throws on one malformed transcript would cost somebody every
/// other session they have, which is a far worse failure than showing one
/// fewer.
/// </para>
/// </summary>
public sealed class SessionHistoryTests : IDisposable
{
    private readonly string _root;
    private readonly FakeEnvironmentProvider _environment;

    public SessionHistoryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "loadout-sessions-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_root);

        _environment = new FakeEnvironmentProvider(_root, new Dictionary<string, string>());
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp tree is not worth failing the run over.
        }
    }

    /// <summary>Writes a Claude transcript where Claude would put one.</summary>
    private string WriteClaudeTranscript(string sessionId, string directory, params string[] lines)
    {
        var folder = Path.Combine(
            _root,
            ".claude",
            "projects",
            directory.Replace(':', '-').Replace(Path.DirectorySeparatorChar, '-'));

        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, sessionId + ".jsonl");

        File.WriteAllLines(path, lines);

        return path;
    }

    /// <summary>
    /// The metadata entry that opens a real Claude transcript.
    /// <para>
    /// Serialised rather than written out as a literal so the fixture is
    /// definitely valid JSON: a test that silently exercises the malformed-line
    /// path proves the opposite of what it claims to.
    /// </para>
    /// </summary>
    private static string ClaudeUserLine(string directory, string branch, string text) =>
        Json(new
        {
            type = "user",
            sessionId = "s",
            cwd = directory,
            gitBranch = branch,
            timestamp = "2026-08-20T10:00:00.000Z",
            message = new
            {
                role = "user",
                content = new[] { new { type = "text", text } },
            },
        });

    private static string Json(object value) =>
        System.Text.Json.JsonSerializer.Serialize(value);

    [Fact]
    public async Task A_claude_transcript_yields_a_resumable_session()
    {
        const string id = "2b7c1d64-0000-4000-8000-000000000001";

        WriteClaudeTranscript(
            id,
            Path.Combine(_root, "code", "alpha"),
            ClaudeUserLine(Path.Combine(_root, "code", "alpha"), "work", "Fix the importer"),
            """{"type":"ai-title","sessionId":"s","aiTitle":"Importer fixes"}""");

        var sessions = await new ClaudeSessionHistory(_environment).ListAsync(10);

        sessions.Succeeded.Should().BeTrue();
        sessions.Value.Should().ContainSingle();

        var session = sessions.Value![0];

        // The identifier is what the agent resumes by, so it has to survive
        // exactly rather than approximately.
        session.SessionId.Should().Be(id);
        session.Agent.Should().Be("claude");
        session.Title.Should().Be("Importer fixes");
        session.Branch.Should().Be("work");
        session.Directory.Should().Be(Path.Combine(_root, "code", "alpha"));
    }

    [Fact]
    public async Task A_later_title_replaces_an_earlier_one()
    {
        WriteClaudeTranscript(
            "00000000-0000-4000-8000-000000000002",
            Path.Combine(_root, "code", "alpha"),
            ClaudeUserLine(Path.Combine(_root, "code", "alpha"), "work", "Something"),
            """{"type":"ai-title","sessionId":"s","aiTitle":"First guess"}""",
            """{"type":"ai-title","sessionId":"s","aiTitle":"What it turned out to be"}""");

        var sessions = await new ClaudeSessionHistory(_environment).ListAsync(10);

        // Claude renames a session as the conversation moves on, and the newest
        // name is the one that describes it.
        sessions.Value![0].Title.Should().Be("What it turned out to be");
    }

    [Fact]
    public async Task Injected_text_is_not_used_to_name_a_session()
    {
        WriteClaudeTranscript(
            "00000000-0000-4000-8000-000000000003",
            Path.Combine(_root, "code", "alpha"),
            ClaudeUserLine(
                Path.Combine(_root, "code", "alpha"),
                "work",
                "<local-command-caveat>Caveat: the messages below were generated by</local-command-caveat>"),
            ClaudeUserLine(Path.Combine(_root, "code", "alpha"), "work", "Rework the upload path"));

        var sessions = await new ClaudeSessionHistory(_environment).ListAsync(10);

        // Naming a session after the tooling's own preamble tells the person
        // scanning the list nothing about which conversation it was.
        sessions.Value![0].Title.Should().Be("Rework the upload path");
    }

    [Fact]
    public async Task A_transcript_without_a_working_directory_is_skipped()
    {
        WriteClaudeTranscript(
            "00000000-0000-4000-8000-000000000004",
            Path.Combine(_root, "code", "alpha"),
            """{"type":"queue-operation","sessionId":"s","timestamp":"2026-08-20T10:00:00Z"}""");

        var sessions = await new ClaudeSessionHistory(_environment).ListAsync(10);

        // Without a directory the session cannot be attributed to a project,
        // and resuming it would start the agent somewhere unexpected.
        sessions.Succeeded.Should().BeTrue();
        sessions.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task A_corrupt_transcript_does_not_cost_the_others()
    {
        WriteClaudeTranscript(
            "00000000-0000-4000-8000-000000000005",
            Path.Combine(_root, "code", "broken"),
            "this is not json",
            "{ neither is this");

        WriteClaudeTranscript(
            "00000000-0000-4000-8000-000000000006",
            Path.Combine(_root, "code", "alpha"),
            ClaudeUserLine(Path.Combine(_root, "code", "alpha"), "work", "Still readable"));

        var sessions = await new ClaudeSessionHistory(_environment).ListAsync(10);

        sessions.Succeeded.Should().BeTrue();
        sessions.Value.Should().ContainSingle()
            .Which.Title.Should().Be("Still readable");
    }

    [Fact]
    public async Task Sessions_come_back_newest_first()
    {
        var older = WriteClaudeTranscript(
            "00000000-0000-4000-8000-00000000000a",
            Path.Combine(_root, "code", "alpha"),
            ClaudeUserLine(Path.Combine(_root, "code", "alpha"), "work", "Older"));

        var newer = WriteClaudeTranscript(
            "00000000-0000-4000-8000-00000000000b",
            Path.Combine(_root, "code", "beta"),
            ClaudeUserLine(Path.Combine(_root, "code", "beta"), "work", "Newer"));

        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddHours(-3));
        File.SetLastWriteTimeUtc(newer, DateTime.UtcNow);

        var sessions = await new ClaudeSessionHistory(_environment).ListAsync(10);

        sessions.Value!.Select(s => s.Title).Should().Equal("Newer", "Older");
    }

    [Fact]
    public async Task No_history_at_all_is_not_an_error()
    {
        var history = new ClaudeSessionHistory(_environment);

        history.IsAvailable.Should().BeFalse();

        var sessions = await history.ListAsync(10);

        // A machine that has never run the agent is a normal machine.
        sessions.Succeeded.Should().BeTrue();
        sessions.Value.Should().BeEmpty();
    }

    /// <summary>Writes a Codex rollout file and the index entry that names it.</summary>
    private void WriteCodexSession(string sessionId, string directory, string? name)
    {
        var folder = Path.Combine(_root, ".codex", "sessions", "2026", "08", "20");

        Directory.CreateDirectory(folder);

        var meta = Json(new
        {
            timestamp = "2026-08-20T10:00:00.000Z",
            type = "session_meta",
            payload = new
            {
                session_id = sessionId,
                cwd = directory,
                cli_version = "0.147.0",
            },
        });

        File.WriteAllLines(
            Path.Combine(folder, $"rollout-2026-08-20T10-00-00-{sessionId}.jsonl"),
            [meta, """{"type":"event_msg","payload":{"type":"task_started"}}"""]);

        if (name is not null)
        {
            File.AppendAllLines(
                Path.Combine(_root, ".codex", "session_index.jsonl"),
                [Json(new
                {
                    id = sessionId,
                    thread_name = name,
                    updated_at = "2026-08-20T10:00:00Z",
                })]);
        }
    }

    [Fact]
    public async Task A_codex_rollout_yields_a_resumable_session()
    {
        const string id = "019fdbaa-9a76-7e11-ad8e-d1b56d225c2b";

        WriteCodexSession(id, Path.Combine(_root, "code", "alpha"), "Fix the site footer");

        var sessions = await new CodexSessionHistory(_environment).ListAsync(10);

        sessions.Succeeded.Should().BeTrue();
        sessions.Value.Should().ContainSingle();

        var session = sessions.Value![0];

        session.SessionId.Should().Be(id);
        session.Agent.Should().Be("codex");

        // The name lives in a separate index from the session itself, so the
        // two being read together is the thing under test.
        session.Title.Should().Be("Fix the site footer");
        session.Directory.Should().Be(Path.Combine(_root, "code", "alpha"));
    }

    [Fact]
    public async Task A_codex_session_with_no_name_falls_back_to_its_folder()
    {
        WriteCodexSession(
            "019fdbaa-0000-7e11-ad8e-d1b56d225c99",
            Path.Combine(_root, "code", "alpha"),
            name: null);

        var sessions = await new CodexSessionHistory(_environment).ListAsync(10);

        // A raw identifier tells nobody which conversation it was.
        sessions.Value![0].Label.Should().Be("alpha");
    }

    [Fact]
    public void A_session_line_is_one_line_whatever_the_title()
    {
        var session = new AgentSession(
            "claude",
            "id",
            new string('x', 400),
            "/home/n/code/alpha",
            "work",
            DateTimeOffset.UtcNow,
            "/transcript.jsonl",
            "alpha");

        var line = SessionDisplay.Line(session, 100);

        // The picker draws one row per session; a title that wraps turns the
        // list into a wall of text.
        line.Length.Should().BeLessThanOrEqualTo(100);
        line.Should().NotContain("\n");
    }

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(90, "1 minute ago")]
    [InlineData(60 * 60 * 5, "5 hours ago")]
    [InlineData(60 * 60 * 24 * 3, "3 days ago")]
    public void Recency_is_said_in_the_roughest_useful_unit(int secondsAgo, string expected) =>
        SessionDisplay.Ago(DateTimeOffset.UtcNow.AddSeconds(-secondsAgo)).Should().Be(expected);

    [Fact]
    public void A_timestamp_in_the_future_does_not_read_as_negative_time()
    {
        // Copied trees and corrected clocks both produce this, and "in 3 hours
        // ago" is worse than rounding it to now.
        SessionDisplay.Ago(DateTimeOffset.UtcNow.AddHours(3)).Should().Be("just now");
    }
}
