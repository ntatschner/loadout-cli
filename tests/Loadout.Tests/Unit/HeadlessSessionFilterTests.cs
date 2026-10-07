using FluentAssertions;
using Loadout.Core.Sessions;
using Loadout.Models.Agents;
using Loadout.Models.Results;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Conversations the launcher started headlessly, kept out of the lists a
/// person resumes from.
/// </summary>
/// <remarks>
/// Team nodes, idea rounds and dump splits each leave a transcript like any
/// other, and before this a project's resume list filled with them. The
/// filter is in the one service every list goes through; these pin that it
/// drops what the ledger names, keeps the rest, and keeps everything for the
/// running view, which needs a live node's transcript to say how long it has
/// been quiet.
/// </remarks>
public sealed class HeadlessSessionFilterTests
{
    private static readonly string Directory = Path.Combine(Path.GetTempPath(), "loadout-filter");

    private static AgentSession Session(string id, int minutesAgo, string agent = "claude") => new(
        agent,
        id,
        id,
        Directory,
        null,
        DateTimeOffset.UtcNow.AddMinutes(-minutesAgo),
        Path.Combine(Directory, id + ".jsonl"));

    private static SessionHistoryService Service(ILaunchLedger ledger, params AgentSession[] sessions) => new(
        [new ListedHistory(sessions)],
        new NoDeclaredHistories(),
        new FakeProjects("starstats", Directory),
        ledger);

    [Fact]
    public async Task A_session_the_ledger_names_as_headless_is_left_out_of_the_list()
    {
        var ledger = new HeadlessLedger(
            new HeadlessSessionRecord("node-1", "claude", "headless launch", "launch-1", DateTimeOffset.UtcNow),
            new HeadlessSessionRecord("idea-1", "claude", "Fleshing out the idea 'x'", null, DateTimeOffset.UtcNow));

        var service = Service(ledger, Session("mine", 30), Session("node-1", 1), Session("idea-1", 2));

        var listed = await service.ListAsync(new SessionQuery());

        listed.Value!.Select(s => s.SessionId).Should().Equal("mine");
    }

    [Fact]
    public async Task The_running_view_still_sees_headless_sessions()
    {
        var ledger = new HeadlessLedger(
            new HeadlessSessionRecord("node-1", "claude", "headless launch", "launch-1", DateTimeOffset.UtcNow));

        var service = Service(ledger, Session("mine", 30), Session("node-1", 1));

        var listed = await service.ListAsync(new SessionQuery(IncludeHeadless: true));

        listed.Value!.Select(s => s.SessionId).Should().Equal("node-1", "mine");
    }

    [Fact]
    public async Task An_identifier_is_matched_for_its_own_agent_only()
    {
        // Two agents' identifiers live in different namespaces. A Codex
        // conversation that happened to share an id with a Claude node is
        // somebody's, and must stay in their list.
        var ledger = new HeadlessLedger(
            new HeadlessSessionRecord("same", "claude", "headless launch", "launch-1", DateTimeOffset.UtcNow));

        var service = new SessionHistoryService(
            [new ListedHistory([Session("same", 1)]), new ListedHistory([Session("same", 2, "codex")], "codex")],
            new NoDeclaredHistories(),
            new FakeProjects("starstats", Directory),
            ledger);

        var listed = await service.ListAsync(new SessionQuery());

        listed.Value!.Should().ContainSingle().Which.Agent.Should().Be("codex");
    }

    [Fact]
    public async Task A_ledger_that_cannot_be_read_hides_nothing()
    {
        // A node in the list is a nuisance. An empty list would be a lie
        // about somebody's history.
        var service = Service(new HeadlessLedger(failing: true), Session("mine", 30), Session("node-1", 1));

        var listed = await service.ListAsync(new SessionQuery());

        listed.Value!.Select(s => s.SessionId).Should().Equal("node-1", "mine");
    }

    private sealed class ListedHistory(IReadOnlyList<AgentSession> sessions, string agent = "claude") : ISessionHistory
    {
        public string Agent => agent;

        public bool IsAvailable => true;

        public Task<OperationResult<IReadOnlyList<AgentSession>>> ListAsync(int limit, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<IReadOnlyList<AgentSession>>.Ok(sessions.Take(limit).ToList()));
    }

    private sealed class NoDeclaredHistories : IDeclaredSessionHistories
    {
        public IReadOnlyList<ISessionHistory> All => [];
    }

    private sealed class HeadlessLedger : ILaunchLedger
    {
        private readonly IReadOnlyList<HeadlessSessionRecord> _sessions;
        private readonly bool _failing;

        public HeadlessLedger(params HeadlessSessionRecord[] sessions)
        {
            _sessions = sessions;
        }

        public HeadlessLedger(bool failing)
        {
            _sessions = [];
            _failing = failing;
        }

        public string Path => "ledger.jsonl";

        public Task<string> RecordStartAsync(NewLaunch launch, CancellationToken ct = default) =>
            throw new NotSupportedException("listing sessions starts nothing");

        public Task RecordEndAsync(string launchId, int? exitCode, CancellationToken ct = default) =>
            throw new NotSupportedException("listing sessions ends nothing");

        public Task RecordHeadlessSessionAsync(
            string sessionId, string agent, string? purpose, string? launchId, CancellationToken ct = default) =>
            throw new NotSupportedException("listing sessions writes nothing");

        public Task<OperationResult<IReadOnlyList<LaunchRecord>>> ReadAsync(
            DateTimeOffset since, CancellationToken ct = default) =>
            throw new NotSupportedException("listing sessions reads no launches");

        public Task<OperationResult<IReadOnlyList<HeadlessSessionRecord>>> ReadHeadlessSessionsAsync(
            CancellationToken ct = default) =>
            Task.FromResult(_failing
                ? OperationResult<IReadOnlyList<HeadlessSessionRecord>>.Fail("the ledger is locked")
                : OperationResult<IReadOnlyList<HeadlessSessionRecord>>.Ok(_sessions));
    }
}
