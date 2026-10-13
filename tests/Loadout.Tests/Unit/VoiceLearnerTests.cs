using FluentAssertions;
using Loadout.Agents.Voice;
using Loadout.Core.Instructions;
using Loadout.Core.Voice;
using Loadout.Models.Voice;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>Learn runs against a scripted agent and a scripted person.</summary>
public sealed class VoiceLearnerTests : IDisposable
{
    /// <summary>Three citations from two kinds of writing: enough to propose under prose/1.</summary>
    private const string Supported =
        """{"contract":"voice-mine/1","rules":[{"id":"british","statement":"Write British spellings.","check":{"type":"prefer-spelling","spellings":[{"avoid":"color","prefer":"colour"}]},"surfaces":[],"citations":[{"evidence":"e1","excerpt":"the colour of it"},{"evidence":"e2","excerpt":"a colour scheme"},{"evidence":"e3","excerpt":"colour the output"}]}]}""";

    /// <summary>Two citations from one kind: short of the threshold, so it is asked about.</summary>
    private const string Thin =
        """{"contract":"voice-mine/1","rules":[{"id":"short-subjects","statement":"Keep commit subjects short.","check":null,"surfaces":["commit"],"citations":[{"evidence":"e3","excerpt":"colour the output"},{"evidence":"e4","excerpt":"Tidy the docs"}]}]}""";

    /// <summary>One citation of the first piece, which is all a one-piece batch can support.</summary>
    private const string FirstPieceOnly =
        """{"contract":"voice-mine/1","rules":[{"id":"british","statement":"Write British spellings.","check":null,"surfaces":[],"citations":[{"evidence":"e1","excerpt":"the colour of it"}]}]}""";

    private readonly TemporaryWorkspace _space = new();
    private readonly string _repository;
    private readonly FakeGit _git;
    private readonly VoiceStore _store;
    private readonly VoiceMarks _marks;

    public VoiceLearnerTests()
    {
        _repository = _space.Scratch("repo");
        _git = new FakeGit(_repository);
        _git.CommitMessages.Add(("1111111111111111111111111111111111111111", "Make the CLI colour the output (#12)\n\nBody."));
        _git.CommitMessages.Add(("2222222222222222222222222222222222222222", "Tidy the docs (#13)"));
        _git.CommitMessages.Add(("3333333333333333333333333333333333333333", "Release 0.1.0"));
        _store = new VoiceStore(_space.Paths.Paths.Config, _space.Paths.Paths.State, _space.Workspace.LocalPath);
        _marks = new VoiceMarks(_git, _store, TimeProvider.System);
    }

    public void Dispose() => _space.Dispose();

    private async Task MarkAsync()
    {
        await File.WriteAllTextAsync(Path.Combine(_repository, "a.md"), "I like the colour of it.");
        await File.WriteAllTextAsync(Path.Combine(_repository, "b.md"), "We chose a colour scheme.");

        foreach (var file in new[] { "a.md", "b.md" })
        {
            await _marks.MarkAsync(_repository, (await _marks.CandidatesAsync(_repository, file)).Value!);
        }
    }

    private VoiceLearner Learner(ScriptedDetachedLauncher launcher) =>
        new(_store, new VoiceEvidenceGatherer(_git, _marks), launcher, _space.Paths, TimeProvider.System);

    private VoiceLearnRequest Request(Prompts prompts, VoiceGuide? guide = null, bool dryRun = false) =>
        new(guide ?? VoiceGuides.Prose(), StyleLayer.Personal, CodingStyles.Personal, null, _repository, null, prompts, DryRun: dryRun);

    private string ProfilePath => _store.PathOf(StyleLayer.Personal, CodingStyles.Personal, "prose", null)!;

    private string StatePath => _store.StatePath(StyleLayer.Personal, CodingStyles.Personal, "prose", null);

    [Fact]
    public async Task Evidence_is_marked_writing_and_squash_merges_and_nothing_else()
    {
        await MarkAsync();

        var gathered = (await new VoiceEvidenceGatherer(_git, _marks).GatherAsync(_repository, null)).Value!;

        gathered.Evidence.Select(e => (e.Id, e.Source)).Should().Equal(
            ("e1", "doc"), ("e2", "doc"), ("e3", "commit"), ("e4", "commit"));
        gathered.Evidence.Should().NotContain(e => e.Text.StartsWith("Release", StringComparison.Ordinal),
            "a commit that is not a squash merge was not necessarily written by hand");
    }

    [Fact]
    public async Task A_first_run_asks_for_a_cap_and_a_supported_rule_is_proposed_and_kept()
    {
        await MarkAsync();
        var prompts = new Prompts { CapAnswer = new VoiceCapAnswer(VoiceCapChoice.Accept) };
        var launcher = new ScriptedDetachedLauncher(ScriptedDetachedLauncher.Result(Supported));

        var learned = await Learner(launcher).LearnAsync(Request(prompts));

        learned.Succeeded.Should().BeTrue(learned.Error);
        prompts.CapQuestions.Should().Equal(VoiceCapQuestion.First);
        prompts.Reviewed.Should().ContainSingle().Which.Asked.Should().BeFalse("three citations from two kinds is enough");

        var run = learned.Value!.Run;
        run.Outcome.Should().Be(VoiceRunOutcome.Complete);
        run.Cap.Should().Be(VoiceCapSetting.Accepted);
        (run.Calls, run.Accepted, run.Score).Should().Be((1, 1, null));

        var profile = (await VoiceStore.ReadProfileAsync(ProfilePath)).Value!;
        profile.Guide.Should().Be("prose/1");
        profile.Rules.Should().ContainSingle().Which.Citations.Should().HaveCount(3)
            .And.OnlyContain(c => c.Hash != null, "every mined citation is pinned to the text it quotes");

        var state = (await VoiceStore.ReadStateAsync(StatePath)).Value!;
        state.Cap!.Calls.Should().Be(2, "one call plus a quarter, rounded up");
        state.Runs.Should().ContainSingle();
    }

    [Fact]
    public async Task A_rule_short_of_the_threshold_is_asked_about_and_kept_on_the_persons_word()
    {
        await MarkAsync();
        var prompts = new Prompts { CapAnswer = new VoiceCapAnswer(VoiceCapChoice.Accept) };

        await Learner(new ScriptedDetachedLauncher(ScriptedDetachedLauncher.Result(Thin))).LearnAsync(Request(prompts));

        prompts.Reviewed.Should().ContainSingle().Which.Asked.Should().BeTrue();

        var rule = (await VoiceStore.ReadProfileAsync(ProfilePath)).Value!.Rules.Single();
        rule.Citations.Should().Contain(c => c.Source == "interview", "the answer is recorded as the reason it was kept");
    }

    [Fact]
    public async Task Declining_the_first_cap_spends_nothing()
    {
        await MarkAsync();
        var prompts = new Prompts { CapAnswer = new VoiceCapAnswer(VoiceCapChoice.Decline) };
        var launcher = new ScriptedDetachedLauncher(ScriptedDetachedLauncher.Result(Supported));

        var learned = await Learner(launcher).LearnAsync(Request(prompts));

        launcher.Started.Should().Be(0);
        learned.Value!.Run.Outcome.Should().Be(VoiceRunOutcome.ReviewOnly);
        learned.Value.Run.Cap.Should().Be(VoiceCapSetting.Declined);
        File.Exists(ProfilePath).Should().BeFalse();
    }

    [Fact]
    public async Task A_run_stopped_at_its_cap_is_partial_and_carries_what_it_mined_to_the_next()
    {
        await MarkAsync();
        var guide = VoiceGuides.Prose();
        guide.Cap.BatchCharacters = 10;
        var prompts = new Prompts
        {
            CapAnswer = new VoiceCapAnswer(VoiceCapChoice.Typed, 0.01m),
            Carry = false,
        };
        var launcher = new ScriptedDetachedLauncher(ScriptedDetachedLauncher.Result(FirstPieceOnly));

        var first = await Learner(launcher).LearnAsync(Request(prompts, guide));

        launcher.Started.Should().Be(1, "a typed cap below one call's cost still allows one call, and no more");
        first.Value!.Run.Outcome.Should().Be(VoiceRunOutcome.Partial);
        first.Value.Run.Cap.Should().Be(VoiceCapSetting.Typed);
        first.Value.Run.Carried.Should().Be(1);
        prompts.Reviewed.Should().BeEmpty("the person said no to more of this run, so it reviews only what it carried in");

        var next = new Prompts { CapAnswer = new VoiceCapAnswer(VoiceCapChoice.Decline) };

        var second = await Learner(new ScriptedDetachedLauncher()).LearnAsync(Request(next, guide));

        next.Reviewed.Should().ContainSingle().Which.Rule.Id.Should().Be("british");
        second.Value!.Run.Outcome.Should().Be(VoiceRunOutcome.ReviewOnly);
        second.Value.Run.Accepted.Should().Be(1);
    }

    [Fact]
    public async Task A_carried_proposal_whose_writing_has_changed_is_dropped_by_name()
    {
        await MarkAsync();
        var prompts = new Prompts { CapAnswer = new VoiceCapAnswer(VoiceCapChoice.Accept), Answer = VoiceReviewChoice.Skip };

        await Learner(new ScriptedDetachedLauncher(ScriptedDetachedLauncher.Result(Supported))).LearnAsync(Request(prompts));

        await File.WriteAllTextAsync(Path.Combine(_repository, "a.md"), "Rewritten since.");

        var next = new Prompts { CapAnswer = new VoiceCapAnswer(VoiceCapChoice.Decline) };

        var learned = await Learner(new ScriptedDetachedLauncher()).LearnAsync(Request(next));

        learned.Value!.Dropped.Should().Equal("british");
        next.Reviewed.Should().BeEmpty();
    }

    [Fact]
    public async Task A_rejected_rule_is_not_put_again_in_the_same_words()
    {
        await MarkAsync();
        var prompts = new Prompts { CapAnswer = new VoiceCapAnswer(VoiceCapChoice.Accept), Answer = VoiceReviewChoice.Reject };

        await Learner(new ScriptedDetachedLauncher(ScriptedDetachedLauncher.Result(Supported))).LearnAsync(Request(prompts));

        var again = new Prompts();

        var learned = await Learner(new ScriptedDetachedLauncher(ScriptedDetachedLauncher.Result(Supported))).LearnAsync(Request(again));

        // The first run's calls cost 0.05, far under the seed the cap was set
        // against, so the cap is offered lower; declining keeps it and the
        // run carries on.
        again.CapQuestions.Should().Equal(VoiceCapQuestion.Lower);
        again.Reviewed.Should().BeEmpty();
        learned.Value!.Run.Outcome.Should().Be(VoiceRunOutcome.Complete);
    }

    [Fact]
    public async Task More_to_read_than_the_cap_allows_asks_before_spending_and_a_no_spends_nothing()
    {
        await MarkAsync();
        var guide = VoiceGuides.Prose();
        await VoiceStore.WriteStateAsync(StatePath, new VoiceLearnState
        {
            Cap = new VoiceCap { Calls = 1, Usd = 0.10m, Estimate = 0.08m },
        });

        guide.Cap.BatchCharacters = 10;
        var prompts = new Prompts { CapAnswer = new VoiceCapAnswer(VoiceCapChoice.Decline) };
        var launcher = new ScriptedDetachedLauncher(ScriptedDetachedLauncher.Result(Supported));

        var learned = await Learner(launcher).LearnAsync(Request(prompts, guide));

        prompts.CapQuestions.Should().Equal(VoiceCapQuestion.Larger);
        launcher.Started.Should().Be(0);
        learned.Value!.Run.Outcome.Should().Be(VoiceRunOutcome.ReviewOnly);
        (await VoiceStore.ReadStateAsync(StatePath)).Value!.Cap!.Usd.Should().Be(0.10m, "a declined replacement leaves the cap alone");
    }

    [Fact]
    public async Task A_dry_run_asks_nothing_starts_nothing_and_writes_nothing()
    {
        await MarkAsync();
        var prompts = new Prompts();
        var launcher = new ScriptedDetachedLauncher(ScriptedDetachedLauncher.Result(Supported));

        var learned = await Learner(launcher).LearnAsync(Request(prompts, dryRun: true));

        learned.Value!.Estimate.Calls.Should().Be(1);
        learned.Value.Estimate.FromHistory.Should().BeFalse();
        prompts.CapQuestions.Should().BeEmpty();
        launcher.Started.Should().Be(0);
        File.Exists(StatePath).Should().BeFalse();
    }

    [Fact]
    public async Task The_agent_reads_with_no_tools_and_answers_in_the_mining_shape()
    {
        await MarkAsync();
        var launcher = new ScriptedDetachedLauncher(ScriptedDetachedLauncher.Result(Supported));

        await Learner(launcher).LearnAsync(Request(new Prompts { CapAnswer = new VoiceCapAnswer(VoiceCapChoice.Accept) }));

        launcher.Options!.AllowedTools.Should().BeEmpty();
        launcher.Options.OutputSchemaJson.Should().Be(VoiceMining.Schema);
        launcher.Pipe!.Written.ToString().Should().Contain("We chose a colour scheme.", "the writing is in the prompt");
    }

    /// <summary>A person who answers the same way every time, and remembers what they were asked.</summary>
    private sealed class Prompts : IVoiceLearnPrompts
    {
        public VoiceCapAnswer CapAnswer { get; init; } = new(VoiceCapChoice.Decline);

        public VoiceReviewChoice Answer { get; init; } = VoiceReviewChoice.Accept;

        public bool Carry { get; init; }

        public List<VoiceCapQuestion> CapQuestions { get; } = [];

        public List<VoiceReviewItem> Reviewed { get; } = [];

        public void Progress(string message)
        {
        }

        public VoiceCapAnswer Cap(VoiceCapQuestion question, VoiceEstimate estimate, VoiceCap proposed, VoiceCap? stored)
        {
            CapQuestions.Add(question);

            return CapAnswer;
        }

        public bool PastCap(VoiceCap cap, int calls, decimal spent, int remaining) => Carry;

        public VoiceReviewAnswer Review(VoiceReviewItem item)
        {
            Reviewed.Add(item);

            return new VoiceReviewAnswer(Answer);
        }
    }
}
