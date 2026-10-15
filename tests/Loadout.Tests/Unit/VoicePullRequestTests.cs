using FluentAssertions;
using Loadout.Core.Git;
using Loadout.Core.Voice;
using Loadout.Models;
using Loadout.Models.Voice;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>Pull requests as marks and as edit pairs, read without reaching GitHub.</summary>
public sealed class VoicePullRequestTests : IDisposable
{
    private readonly TemporaryWorkspace _space = new();
    private readonly FakePullRequests _pulls = new();
    private readonly string _repository;
    private readonly FakeGit _git;
    private readonly VoiceMarks _marks;

    public VoicePullRequestTests()
    {
        _repository = _space.Scratch("repo");
        _git = new FakeGit(_repository);
        _marks = new VoiceMarks(
            _git, _pulls, new VoiceStore(_space.Paths.Paths.Config, _space.Paths.Paths.State, null), TimeProvider.System);
    }

    public void Dispose() => _space.Dispose();

    [Theory]
    [InlineData("#12", 12)]
    [InlineData("pr:12", 12)]
    [InlineData("PR:7", 7)]
    [InlineData("12", null)]
    [InlineData("#twelve", null)]
    [InlineData("main~3..main", null)]
    public void A_pull_request_is_named_by_number(string target, int? expected) =>
        VoiceMarks.PullRequestNumber(target).Should().Be(expected);

    [Fact]
    public async Task A_marked_pull_request_lapses_when_its_description_is_edited()
    {
        _pulls.Known[12] = new(12, "Colour the output", "Written by hand.");

        var candidates = (await _marks.CandidatesAsync(_repository, "pr:12")).Value!;
        candidates.Should().ContainSingle().Which.Should().Be(
            new VoiceMarkCandidate(VoiceMarkKind.PullRequest, "#12", "Colour the output\n\nWritten by hand."));

        await _marks.MarkAsync(_repository, candidates);
        (await _marks.StandingAsync(_repository)).Value!.Single().State.Should().Be(VoiceMarkState.Holds);

        _pulls.Known[12] = new(12, "Colour the output", "Edited since.");
        (await _marks.StandingAsync(_repository)).Value!.Single().State.Should().Be(VoiceMarkState.Lapsed);

        var lapsed = (await _marks.LapsedAsync(_repository)).Value!;
        lapsed.Should().ContainSingle().Which.Text.Should().EndWith("Edited since.");

        _pulls.Unavailable = "gh is signed out";
        (await _marks.StandingAsync(_repository)).Value!.Single().State.Should().Be(
            VoiceMarkState.Missing, "a pull request that cannot be read now is not counted, the same as a missing file");
    }

    [Fact]
    public async Task A_marked_pull_request_is_evidence_of_its_own_kind()
    {
        _pulls.Known[12] = new(12, "Colour the output", "Written by hand.");
        await _marks.MarkAsync(_repository, (await _marks.CandidatesAsync(_repository, "#12")).Value!);

        var gathered = (await new VoiceEvidenceGatherer(_git, _pulls, _marks).GatherAsync(_repository, null)).Value!;

        gathered.Evidence.Should().ContainSingle().Which.Should().Match<VoiceEvidence>(
            e => e.Source == "pr" && e.Reference == "#12");
    }

    [Fact]
    public async Task Only_a_description_rewritten_at_merge_is_an_edit_pair()
    {
        _git.CommitMessages.Add(("a1", "Colour the output (#12)\n\nKept short."));
        _git.CommitMessages.Add(("a2", "Tidy the docs (#13)"));
        _git.CommitMessages.Add(("a3", "Merge as drafted (#14)\n\nThe same words."));
        _git.CommitMessages.Add(("a4", "Not from a pull request in the list (#15)\n\nBody."));

        _pulls.Known[12] = new(12, "Colour the output", "A long draft about colour.");
        _pulls.Known[13] = new(13, "Tidy the docs", string.Empty);
        _pulls.Known[14] = new(14, "Merge as drafted (#14)", "The  same\nwords.");

        var gatherer = new VoiceEvidenceGatherer(_git, _pulls, _marks);

        var without = (await gatherer.GatherAsync(_repository, null)).Value!;
        without.EditPairs.Should().BeEmpty();
        _pulls.Reads.Should().Be(0, "pull requests are read only when asked for");

        var with = (await gatherer.GatherAsync(_repository, null, pullRequests: true)).Value!;

        with.EditPairs.Should().ContainSingle().Which.Should().Be(
            new VoiceEditPair(12, "a1", "Colour the output\n\nA long draft about colour.", "Colour the output (#12)\n\nKept short."));
        with.Evidence.Should().HaveCount(4, "edit pairs are scored against, not mined, so evidence is unchanged");
    }

    [Theory]
    [InlineData("Fix it (#1)\n\nCo-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>", true)]
    [InlineData("Fix it (#1)\n\nco-authored-by: Copilot <copilot@github.com>", true)]
    [InlineData("Fix it (#1)\n\nCo-authored-by: dependabot[bot] <support@github.com>", true)]
    [InlineData("Fix it (#1)\n\n🤖 Generated with [Claude Code](https://claude.com/claude-code)", true)]
    [InlineData("Fix it (#1)\n\nCo-Authored-By: Sam Jones <sam@example.com>", false)]
    [InlineData("Ask Claude about the colour (#1)\n\nWritten by hand.", false)]
    public void An_assistant_is_known_by_its_trailer_not_by_being_mentioned(string message, bool expected) =>
        VoiceEvidenceGatherer.ByAnAssistant(message).Should().Be(expected);

    [Fact]
    public async Task A_squash_merge_an_assistant_co_wrote_is_neither_evidence_nor_an_edit()
    {
        _git.CommitMessages.Add(("a1", "Colour the output (#12)\n\nKept short.\n\nCo-Authored-By: Claude <noreply@anthropic.com>"));
        _git.CommitMessages.Add(("a2", "Tidy the docs (#13)\n\nBy hand."));
        _pulls.Known[12] = new(12, "Colour the output", "A long draft about colour.");
        _pulls.Known[13] = new(13, "Tidy the docs", "A draft about docs.");

        var gathered = (await new VoiceEvidenceGatherer(_git, _pulls, _marks).GatherAsync(_repository, null, pullRequests: true)).Value!;

        gathered.Evidence.Should().ContainSingle().Which.Reference.Should().Be("a2");
        gathered.EditPairs.Should().ContainSingle().Which.Number.Should().Be(13);
        gathered.AgentWritten.Should().Be(1);
        gathered.Unconfirmed.Should().Be(1, "a2 is read on the assumption it is the person's");
        gathered.OnlyUnconfirmed.Should().BeTrue();
    }

    [Fact]
    public async Task Gh_is_asked_only_to_read()
    {
        var launcher = new StubProcessLauncher("""[{"number":12,"title":"Colour","body":"Draft."},{"number":11,"title":"Older","body":null}]""");
        var reader = new GhPullRequestReader(launcher, new StubResolver("gh"));

        var merged = await reader.MergedAsync(_repository, 50);

        merged.Value!.Should().Equal(new PullRequestText(12, "Colour", "Draft."), new PullRequestText(11, "Older", string.Empty));
        launcher.Requests.Single().Arguments.Should().Equal("pr", "list", "--state", "merged", "--limit", "50", "--json", "number,title,body");
        launcher.Requests.Single().WorkingDirectory.Should().Be(_repository);
    }

    [Fact]
    public async Task Without_gh_a_pull_request_cannot_be_read_and_says_so()
    {
        var reader = new GhPullRequestReader(new StubProcessLauncher("{}"), new StubResolver(null));

        var read = await reader.ReadAsync(_repository, 12);

        read.Failed.Should().BeTrue();
        read.ExitCode.Should().Be(ExitCode.AgentUnavailable);
        read.Error.Should().Contain("gh auth login");
    }
}
