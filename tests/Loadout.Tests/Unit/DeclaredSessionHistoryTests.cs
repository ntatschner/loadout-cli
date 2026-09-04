using FluentAssertions;
using Loadout.Core.Sessions;
using Loadout.Models.Agents;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Reading an agent's sessions from a description of its transcripts.
/// </summary>
/// <remarks>
/// <para>
/// The first test is the one that matters. A reader driven by configuration can
/// be made to pass any test written alongside it, which proves only that the
/// test and the reader agree. So it is pointed at a real format — Codex's — and
/// checked against the compiled reader for that format, over the same files. An
/// instrument is worth believing once it has answered a case whose answer was
/// already known.
/// </para>
/// <para>
/// The rest cover what the description has to get right on its own, and one test
/// records what it deliberately cannot express.
/// </para>
/// </remarks>
public sealed class DeclaredSessionHistoryTests : IDisposable
{
    private readonly string _root;
    private readonly FakeEnvironmentProvider _environment;

    public DeclaredSessionHistoryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "loadout-declared-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_root);

        _environment = new FakeEnvironmentProvider(Path.Combine(_root, "home"));
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

    [Fact]
    public async Task A_described_format_finds_what_the_compiled_reader_for_it_finds()
    {
        WriteCodexRollout("2026/02/01", "rollout-one.jsonl", "sess-one", "/repos/starstats");
        WriteCodexRollout("2026/02/02", "rollout-two.jsonl", "sess-two", "/repos/storefront");

        var compiled = await new CodexSessionHistory(_environment).ListAsync(10);
        var declared = await Reader("codex", CodexFormat()).ListAsync(10);

        compiled.Succeeded.Should().BeTrue(compiled.Error);
        declared.Succeeded.Should().BeTrue(declared.Error);

        compiled.Value!.Should().NotBeEmpty("the fixture has to exercise the compiled reader too");

        declared.Value!.Select(session => (session.SessionId, session.Directory, session.TranscriptPath))
            .Should().Equal(compiled.Value!.Select(
                session => (session.SessionId, session.Directory, session.TranscriptPath)));
    }

    [Fact]
    public async Task A_title_kept_in_a_separate_index_cannot_be_described()
    {
        // Codex keeps session names in session_index.jsonl, beside the rollouts
        // rather than inside them, and the description has no way to say "join
        // these two files on an identifier". Recorded here so the limit is
        // known rather than discovered by somebody whose sessions all list as
        // directories. Adding a join is a bigger idea than this format is, and
        // should be asked for before it is built.
        WriteCodexRollout("2026/02/01", "rollout-one.jsonl", "sess-one", "/repos/starstats");

        await File.WriteAllTextAsync(
            Path.Combine(_environment.HomeDirectory, ".codex", "session_index.jsonl"),
            "{\"id\":\"sess-one\",\"thread_name\":\"fix the upload path\"}\n");

        var compiled = await new CodexSessionHistory(_environment).ListAsync(10);
        var declared = await Reader("codex", CodexFormat()).ListAsync(10);

        compiled.Value!.Single().Title.Should().Be("fix the upload path");
        declared.Value!.Single().Title.Should().BeNull();
    }

    [Fact]
    public async Task A_value_anywhere_in_the_file_is_found_when_the_format_says_so()
    {
        // Formats differ on this and it cannot be guessed: one opens with a
        // metadata entry, another repeats the working directory on every line.
        WriteLines("logs", "session.jsonl",
            "{\"type\":\"banner\"}",
            "{\"sessionId\":\"sess-nine\"}",
            "{\"cwd\":\"/repos/late\"}");

        var format = new TranscriptFormat
        {
            Root = Path.Combine(_root, "logs"),
            Files = "*.jsonl",
            Session = new TranscriptSessionFormat
            {
                Id = "sessionId",
                Directory = "cwd",
                FirstLineOnly = false,
            },
        };

        var session = (await Reader("scribe", format).ListAsync(10)).Value!.Should().ContainSingle().Subject;

        session.SessionId.Should().Be("sess-nine");
        session.Directory.Should().Be("/repos/late");
    }

    [Fact]
    public async Task Reading_stops_at_the_first_line_when_the_format_says_so()
    {
        // The point of saying so: a listing must not parse a whole conversation
        // to put a name on a menu.
        WriteLines("logs", "session.jsonl",
            "{\"type\":\"banner\"}",
            "{\"sessionId\":\"sess-nine\",\"cwd\":\"/repos/late\"}");

        var format = CodexFormat();
        format.Root = Path.Combine(_root, "logs");
        format.Files = "*.jsonl";
        format.Session = new TranscriptSessionFormat
        {
            Id = "sessionId",
            Directory = "cwd",
            FirstLineOnly = true,
        };

        (await Reader("scribe", format).ListAsync(10)).Value!.Should().BeEmpty();
    }

    [Fact]
    public async Task The_earliest_value_wins_when_a_field_repeats()
    {
        // Where the session started, not where it ended up. A later line
        // overwriting this would rename a session halfway through its own list.
        //
        // The identifier is alone on the first line on purpose: reading stops as
        // soon as both values are in hand, so a fixture carrying both on line
        // one never reaches a second line and proves nothing about which wins.
        WriteLines("logs", "session.jsonl",
            "{\"sessionId\":\"sess-one\"}",
            "{\"sessionId\":\"sess-two\",\"cwd\":\"/repos/first\"}");

        var format = new TranscriptFormat
        {
            Root = Path.Combine(_root, "logs"),
            Session = new TranscriptSessionFormat
            {
                Id = "sessionId",
                Directory = "cwd",
                FirstLineOnly = false,
            },
        };

        var session = (await Reader("scribe", format).ListAsync(10)).Value!.Single();

        session.SessionId.Should().Be("sess-one");
        session.Directory.Should().Be("/repos/first");
    }

    [Fact]
    public async Task A_line_that_is_not_json_costs_that_line_only()
    {
        WriteLines("logs", "session.jsonl",
            "not json at all",
            "{\"sessionId\":\"sess-one\",\"cwd\":\"/repos/first\"}");

        var format = new TranscriptFormat
        {
            Root = Path.Combine(_root, "logs"),
            Session = new TranscriptSessionFormat
            {
                Id = "sessionId",
                Directory = "cwd",
                FirstLineOnly = false,
            },
        };

        (await Reader("scribe", format).ListAsync(10)).Value!.Should().ContainSingle();
    }

    [Fact]
    public async Task A_file_missing_either_required_value_is_skipped_rather_than_half_reported()
    {
        WriteLines("logs", "nameless.jsonl", "{\"cwd\":\"/repos/first\"}");
        WriteLines("logs", "placeless.jsonl", "{\"sessionId\":\"sess-one\"}");

        var format = new TranscriptFormat
        {
            Root = Path.Combine(_root, "logs"),
            Session = new TranscriptSessionFormat
            {
                Id = "sessionId",
                Directory = "cwd",
                FirstLineOnly = false,
            },
        };

        (await Reader("scribe", format).ListAsync(10)).Value!.Should().BeEmpty();
    }

    [Fact]
    public async Task Nested_directories_are_searched_only_when_the_format_asks()
    {
        WriteLines(Path.Combine("logs", "deep"), "session.jsonl",
            "{\"sessionId\":\"sess-one\",\"cwd\":\"/repos/first\"}");

        var format = new TranscriptFormat
        {
            Root = Path.Combine(_root, "logs"),
            Recursive = false,
            Session = new TranscriptSessionFormat { Id = "sessionId", Directory = "cwd" },
        };

        (await Reader("scribe", format).ListAsync(10)).Value!.Should().BeEmpty();

        format.Recursive = true;

        (await Reader("scribe", format).ListAsync(10)).Value!.Should().ContainSingle();
    }

    [Fact]
    public async Task A_home_relative_root_is_resolved_against_the_environment()
    {
        // Never against the real home directory. A reader that reached the
        // machine running the suite would pass here and read somebody's actual
        // transcripts, which is both wrong and a way to pass by accident.
        WriteCodexRollout("2026/02/01", "rollout-one.jsonl", "sess-one", "/repos/starstats");

        (await Reader("codex", CodexFormat()).ListAsync(10)).Value!.Should().ContainSingle();
    }

    [Fact]
    public void An_incomplete_description_reads_nothing_rather_than_guessing()
    {
        // The directory has to exist, or this passes because there is nothing
        // there rather than because the description is incomplete — which is
        // the same answer for the wrong reason.
        Directory.CreateDirectory(Path.Combine(_root, "logs"));

        var format = new TranscriptFormat { Root = Path.Combine(_root, "logs") };

        format.IsUsable.Should().BeFalse("no field paths were given");

        Reader("scribe", format).IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task Optional_paths_are_taken_when_the_format_names_them()
    {
        WriteLines("logs", "session.jsonl",
            "{\"sessionId\":\"s\",\"cwd\":\"/repos/first\","
            + "\"meta\":{\"title\":\"fix the upload path\",\"branch\":\"work\"}}");

        var format = new TranscriptFormat
        {
            Root = Path.Combine(_root, "logs"),
            Session = new TranscriptSessionFormat
            {
                Id = "sessionId",
                Directory = "cwd",
                Title = "meta.title",
                Branch = "meta.branch",
            },
        };

        var session = (await Reader("scribe", format).ListAsync(10)).Value!.Single();

        session.Title.Should().Be("fix the upload path");
        session.Branch.Should().Be("work");
    }

    private DeclaredSessionHistory Reader(string agent, TranscriptFormat format) =>
        new(agent, format, _environment);

    /// <summary>Codex's layout, written as configuration rather than code.</summary>
    private static TranscriptFormat CodexFormat() => new()
    {
        Root = "~/.codex/sessions",
        Files = "rollout-*.jsonl",
        Recursive = true,
        Session = new TranscriptSessionFormat
        {
            Id = "payload.session_id",
            Directory = "payload.cwd",
            FirstLineOnly = true,
        },
    };

    private void WriteCodexRollout(string day, string file, string id, string cwd)
    {
        var directory = Path.Combine(
            _environment.HomeDirectory, ".codex", "sessions", day.Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(directory);

        File.WriteAllText(
            Path.Combine(directory, file),
            $"{{\"payload\":{{\"session_id\":\"{id}\",\"cwd\":\"{cwd}\"}}}}\n");
    }

    private void WriteLines(string relative, string file, params string[] lines)
    {
        var directory = Path.Combine(_root, relative);

        Directory.CreateDirectory(directory);

        File.WriteAllLines(Path.Combine(directory, file), lines);
    }
}
