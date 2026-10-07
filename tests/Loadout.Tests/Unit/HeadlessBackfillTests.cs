using FluentAssertions;
using Loadout.Core.Sessions;
using Loadout.Core.Teams;
using Loadout.Models.Platform;
using Loadout.Platform.Linux;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The nodes of runs from before the ledger wrote their conversations down,
/// caught up on from the runs' own journals.
/// </summary>
/// <remarks>
/// Against a real journal, ledger and Claude folder in a temporary home, so
/// what is pinned is what the launcher will find on a machine upgraded with
/// runs already on it.
/// </remarks>
public sealed class HeadlessBackfillTests : IDisposable
{
    private readonly string _root;
    private readonly LinuxPaths _paths;
    private readonly LaunchLedger _ledger;
    private readonly HeadlessTranscripts _transcripts;
    private readonly RunJournal _journal;

    public HeadlessBackfillTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "loadout-backfill-" + Guid.NewGuid().ToString("N"));

        var environment = new FakeEnvironmentProvider(
            Path.Combine(_root, "home"),
            new Dictionary<string, string>
            {
                ["CLAUDE_CONFIG_DIR"] = Path.Combine(_root, "claude"),
                ["XDG_CONFIG_HOME"] = Path.Combine(_root, "config"),
                ["XDG_DATA_HOME"] = Path.Combine(_root, "data"),
                ["XDG_STATE_HOME"] = Path.Combine(_root, "state"),
                ["XDG_CACHE_HOME"] = Path.Combine(_root, "cache"),
            });

        var permissions = new NoOpFilePermissions();

        _paths = new LinuxPaths(
            environment,
            permissions,
            new HostPlatform(
                HostOperatingSystem.Linux,
                System.Runtime.InteropServices.Architecture.X64,
                "test",
                "TEST-MACHINE"));

        _paths.EnsureDirectoriesExist();

        _ledger = new LaunchLedger(_paths, permissions, TimeProvider.System);
        _transcripts = new HeadlessTranscripts(environment, _paths, TimeProvider.System);
        _journal = new RunJournal(_paths);
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

    private HeadlessBackfill Backfill() => new(_journal, _ledger, _transcripts, _paths);

    /// <summary>A run that reported two sessions for one node, and ended or did not.</summary>
    private void Run(string id, bool ended, params string[] sessions)
    {
        var directory = Path.Combine(_paths.Paths.State, "teams", "runs", id);
        Directory.CreateDirectory(directory);

        var lines = new List<string>
        {
            """{"at":"2026-09-01T09:00:00+00:00","run":"r","node":null,"kind":"run.started","data":{"team":"t","goal":"g","autonomy":"autonomous","rounds":1}}""",
        };

        lines.AddRange(sessions.Select(session =>
            $$$"""{"at":"2026-09-01T09:00:40+00:00","run":"r","node":"lead","kind":"node.turn","data":{"round":1,"turns":1,"cost":0.1,"session":"{{{session}}}"}}"""));

        if (ended)
        {
            lines.Add("""{"at":"2026-09-01T09:05:00+00:00","run":"r","node":null,"kind":"run.finished","data":{"ended":"done","cost":0.1,"rounds":1}}""");
        }

        File.WriteAllLines(Path.Combine(directory, "journal.jsonl"), lines);
    }

    private string Transcript(string session)
    {
        var folder = Path.Combine(_root, "claude", "projects", "D--git-repo");
        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, session + ".jsonl");
        File.WriteAllText(path, "a node's conversation");

        return path;
    }

    [Fact]
    public async Task Every_session_a_journal_names_is_written_down_and_an_ended_run_s_are_put_away()
    {
        // A resumed node reports a second session; both are its.
        Run("20260901-0900-aaaa", ended: true, "first", "resumed");
        var first = Transcript("first");
        var resumed = Transcript("resumed");

        await Backfill().RunOnceAsync();

        var written = (await _ledger.ReadHeadlessSessionsAsync()).Value!;

        written.Select(s => s.SessionId).Should().BeEquivalentTo(["first", "resumed"]);
        written.Should().OnlyContain(s => s.Agent == "claude" && s.LaunchId == null);

        File.Exists(first).Should().BeFalse();
        File.Exists(resumed).Should().BeFalse();
    }

    [Fact]
    public async Task A_run_still_going_has_its_sessions_written_down_and_its_transcripts_left_alone()
    {
        Run("20260901-0900-bbbb", ended: false, "live");
        var live = Transcript("live");

        await Backfill().RunOnceAsync();

        (await _ledger.ReadHeadlessSessionsAsync()).Value!.Should().ContainSingle(s => s.SessionId == "live");
        File.Exists(live).Should().BeTrue("a node still running is still writing there");
    }

    [Fact]
    public async Task It_is_done_once_on_a_machine()
    {
        Run("20260901-0900-cccc", ended: true, "once");

        await Backfill().RunOnceAsync();

        // A second launcher process, later: the mark says it is done.
        Run("20260901-0900-dddd", ended: true, "later");
        await Backfill().RunOnceAsync();

        (await _ledger.ReadHeadlessSessionsAsync()).Value!
            .Select(s => s.SessionId).Should().Equal(["once"]);
    }
}
