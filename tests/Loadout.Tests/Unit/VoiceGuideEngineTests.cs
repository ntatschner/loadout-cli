using System.Text.Json;
using FluentAssertions;
using Loadout.Agents.Voice;
using Loadout.Core.Agents;
using Loadout.Core.Instructions;
using Loadout.Core.Voice;
using Loadout.Models.Voice;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// One engine for every guide: a drafted guide found by name, gathering only
/// what it lists, your prompts to Claude Code as a source, and every guide's
/// rules rendered and checked together.
/// </summary>
public sealed class VoiceGuideEngineTests : IDisposable
{
    private readonly TemporaryWorkspace _space = new();
    private readonly FakePullRequests _pulls = new();
    private readonly string _repository;
    private readonly string _claude;
    private readonly FakeGit _git;
    private readonly VoiceStore _store;
    private readonly VoiceMarks _marks;

    public VoiceGuideEngineTests()
    {
        _repository = _space.Scratch("repo");
        _claude = _space.Scratch("claude");
        _git = new FakeGit(_repository);
        _git.CommitMessages.Add(("1111111111111111111111111111111111111111", "Make the CLI colour the output (#12)"));
        _store = new VoiceStore(_space.Paths.Paths.Config, _space.Paths.Paths.State, _space.Workspace.LocalPath);
        _marks = new VoiceMarks(_git, _pulls, _store, TimeProvider.System);
    }

    public void Dispose() => _space.Dispose();

    /// <summary>A guide about word choice that reads your prompts and nothing from the repository.</summary>
    private static VoiceGuide Idiolect(int version = 1, params string[] sources) => new()
    {
        Id = "idiolect",
        Version = version,
        Summary = "The words and phrases you reach for.",
        Sources = sources.Length > 0 ? [.. sources] : [VoiceGuideDrafting.Transcript],
        Surfaces = [VoiceGuides.Document, VoiceGuides.Commit, VoiceGuides.PullRequest],
        CheckTypes = [.. VoiceGuideDrafting.TranscriptChecks],
        Evidence = new VoiceEvidenceThreshold { Citations = 2, Sources = 1 },
    };

    private VoiceTranscripts Transcripts() =>
        new(new FakeEnvironmentProvider(_space.Scratch("home"), new Dictionary<string, string> { [AgentHome.Override] = _claude }));

    private VoiceEvidenceGatherer Gatherer() => new(_git, _pulls, _marks, Transcripts());

    // --------------------------------------------------------- transcript lines

    private static string User(string text, string? source = "typed", string origin = "human", bool meta = false, bool sidechain = false) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["type"] = "user",
            ["promptSource"] = source,
            ["origin"] = new { kind = origin },
            ["isMeta"] = meta,
            ["isSidechain"] = sidechain,
            ["message"] = new { role = "user", content = text },
        });

    private static string Reply() =>
        """{"type":"assistant","message":{"role":"assistant","content":[{"type":"text","text":"Done."}]}}""";

    private async Task SessionAsync(string project, string session, params string[] lines)
    {
        var directory = Path.Combine(_claude, "projects", project);
        Directory.CreateDirectory(directory);
        await File.WriteAllLinesAsync(Path.Combine(directory, session + ".jsonl"), lines);
    }

    // ------------------------------------------------------------- transcripts

    [Fact]
    public void Only_prompts_a_person_typed_are_read()
    {
        var text = VoiceTranscripts.TypedText(
        [
            User("the opening prompt"),
            Reply(),
            User("typed by hand"),
            User("queued while it worked", source: "queued"),
            User("sent by a program", source: "sdk", origin: "sdk"),
            User("sent through the SDK on a person's behalf", source: "sdk"),
            User("typed, but by another session", origin: "peer"),
            User("a task finished", source: "system", origin: "task-notification"),
            User("a suggestion taken", source: "suggestion_accepted"),
            User("injected by the tooling", meta: true),
            User("a subagent's prompt", sidechain: true),
            User("no source recorded", source: null),
            "not json at all",
        ]);

        text.Should().Be("typed by hand\n\nqueued while it worked");
    }

    [Fact]
    public void A_sessions_opening_prompt_is_never_read_because_loadout_may_have_given_it()
    {
        VoiceTranscripts.TypedText([User("Onboard this project"), Reply(), User("my own words")])
            .Should().Be("my own words");

        VoiceTranscripts.TypedText([User("Onboard this project")]).Should().BeNull();

        // After a reply nothing was given at launch, so the first prompt is the person's.
        VoiceTranscripts.TypedText([Reply(), User("my own words")]).Should().Be("my own words");
    }

    [Fact]
    public void Pasted_text_and_code_are_taken_out_and_markup_is_dropped()
    {
        VoiceTranscripts.Clean(
                """before <pasted_content id="a1">somebody else's text</pasted_content id="a1"> after""")
            .Should().Be("before   after");

        VoiceTranscripts.Clean("""mine <pasted_content id="b2">pasted and never closed""").Should().Be("mine");

        VoiceTranscripts.Clean("look at this ```var x = 1;``` and this").Should().Be("look at this   and this");

        VoiceTranscripts.Clean("<command-name>/clear</command-name>").Should().BeNull();
    }

    [Theory]
    [InlineData("PS D:\\git\\talkwatch> dotnet run --project tools/Capture -- cert https://192.168.1.1\nerror: No certificate was presented.")]
    [InlineData("C:\\work> loadout voice check")]
    [InlineData("me@box:~/src$ make")]
    [InlineData("fail: Microsoft.Hosting[1]")]
    [InlineData("Unhandled exception. System.InvalidOperationException: Sequence contains more than one element\n   at System.Linq.Enumerable.Single[T](IEnumerable`1 source)\n   at Program.Main()")]
    [InlineData("    at Object.<anonymous> (/app/index.js:3:9)")]
    [InlineData("Traceback (most recent call last):\n  File \"main.py\", line 4, in <module>\nKeyError: 'x'")]
    [InlineData("src/Foo.cs(12,5): error CS1002: ; expected")]
    [InlineData("[Image #1]")]
    [InlineData("C:\\Users\\me\\Pictures\\Screenshot 2026-10-15 101112.png")]
    [InlineData("\"/home/me/Pictures/Screen Shot.jpg\"")]
    public void Terminal_output_and_screenshots_are_taken_out(string pasted) =>
        VoiceTranscripts.Clean(pasted).Should().BeEmpty();

    [Fact]
    public void The_persons_words_around_an_unwrapped_error_are_kept()
    {
        VoiceTranscripts.Clean(
                "this fails when i run it\nerror: Could not find file 'talk.har'.\n[Image #1] and the screen looks wrong")
            .Should().Be("this fails when i run it\n  and the screen looks wrong");

        VoiceTranscripts.Clean("the error says it can't find the file, at least I think so")
            .Should().Be("the error says it can't find the file, at least I think so", "talking about an error is not pasting one");

        VoiceTranscripts.Clean("compare this with screenshot.pngs elsewhere")
            .Should().Be("compare this with screenshot.pngs elsewhere");
    }

    [Fact]
    public async Task Transcripts_are_read_from_where_claude_keeps_them_one_session_a_piece()
    {
        await SessionAsync("repo-a", "11111111-aaaa", User("opening"), Reply(), User("colour it in"));
        await SessionAsync("repo-a", "agent-subagent", User("opening"), Reply(), User("written by an agent"));
        await SessionAsync("repo-b", "22222222-bbbb", User("only an opening prompt"));

        var nested = Path.Combine(_claude, "projects", "repo-a", "11111111-aaaa", "subagents");
        Directory.CreateDirectory(nested);
        await File.WriteAllLinesAsync(Path.Combine(nested, "deeper.jsonl"), [User("opening"), Reply(), User("deeper still")]);

        var read = await Transcripts().ReadAsync();

        read.Should().ContainSingle().Which.Should().Be(new VoiceTranscript("11111111-aaaa", "colour it in"));
    }

    // ---------------------------------------------------------------- gathering

    [Fact]
    public async Task Gathering_reads_only_the_sources_the_guide_lists()
    {
        await File.WriteAllTextAsync(Path.Combine(_repository, "a.md"), "I like the colour of it.");
        await _marks.MarkAsync(_repository, (await _marks.CandidatesAsync(_repository, "a.md")).Value!);
        await SessionAsync("repo", "33333333-cccc", User("opening"), Reply(), User("colour it in"));

        var marksOnly = await Gatherer().GatherAsync(new VoiceGatherRequest(_repository, null, ["mark"]));
        marksOnly.Value!.Evidence.Select(e => e.Source).Should().Equal(VoiceGuides.Document);

        var squashOnly = await Gatherer().GatherAsync(new VoiceGatherRequest(_repository, null, ["squash"]));
        squashOnly.Value!.Evidence.Select(e => e.Source).Should().Equal(VoiceGuides.Commit);

        var prompts = await Gatherer().GatherAsync(
            new VoiceGatherRequest(_repository, null, [VoiceGuideDrafting.Transcript], Transcripts: true));
        prompts.Value!.Evidence.Should().ContainSingle().Which.Should().Match<VoiceEvidence>(e =>
            e.Source == VoiceEvidenceGatherer.Prompt && e.Reference == "33333333-cccc" && e.Text == "colour it in");
    }

    [Fact]
    public async Task Squash_merges_walked_for_edit_pairs_are_not_learned_from_unless_the_guide_lists_them()
    {
        _pulls.Known[12] = new(12, "Colour the output", "A long draft about colour.");

        var gathered = await Gatherer().GatherAsync(
            new VoiceGatherRequest(_repository, null, ["mark", "edit-pair"], PullRequests: true));

        gathered.Failed.Should().BeFalse(gathered.Error);
        gathered.Value!.EditPairs.Should().ContainSingle();
        gathered.Value.Evidence.Should().BeEmpty("the guide learns from marks, and nothing is marked");
    }

    [Fact]
    public async Task Transcripts_are_read_only_when_asked_for_and_redacted_like_everything_else()
    {
        var token = "ghp_" + new string('a', 36);
        await SessionAsync("repo", "44444444-dddd", User("opening"), Reply(), User($"use {token} for it"));

        var notAsked = await Gatherer().GatherAsync(new VoiceGatherRequest(null, null, [VoiceGuideDrafting.Transcript]));
        notAsked.Value!.Evidence.Should().BeEmpty();

        var asked = await Gatherer().GatherAsync(
            new VoiceGatherRequest(null, null, [VoiceGuideDrafting.Transcript], Transcripts: true));
        asked.Value!.Evidence.Should().ContainSingle().Which.Text.Should().NotContain(token);
    }

    [Fact]
    public async Task A_source_the_guide_does_not_list_is_refused_rather_than_read()
    {
        var transcripts = await Gatherer().GatherAsync(new VoiceGatherRequest(null, null, ["mark"], Transcripts: true));
        transcripts.Failed.Should().BeTrue();
        transcripts.Error.Should().Contain("transcripts");

        var corpus = await Gatherer().GatherAsync(new VoiceGatherRequest(null, _space.Scratch("corpus"), ["mark"]));
        corpus.Failed.Should().BeTrue();
        corpus.Error.Should().Contain("corpus");

        var pairs = await Gatherer().GatherAsync(new VoiceGatherRequest(_repository, null, ["squash"], PullRequests: true));
        pairs.Failed.Should().BeTrue();
        pairs.Error.Should().Contain("edit pairs");
    }

    [Fact]
    public async Task A_learn_run_hands_the_guide_and_the_transcript_choice_to_gathering()
    {
        await SessionAsync("repo", "55555555-eeee", User("opening"), Reply(), User("colour it in"));

        var learner = new VoiceLearner(_store, Gatherer(), new ScriptedDetachedLauncher(), _space.Paths, TimeProvider.System);

        var learned = await learner.LearnAsync(new VoiceLearnRequest(
            Idiolect(),
            StyleLayer.Personal,
            CodingStyles.Personal,
            null,
            _repository,
            null,
            new NoPrompts(),
            DryRun: true,
            Transcripts: true));

        learned.Failed.Should().BeFalse(learned.Error);
        learned.Value!.Gathered.Evidence.Select(e => e.Source).Should().Equal(VoiceEvidenceGatherer.Prompt);
        learned.Value.ProfilePath.Should().EndWith("idiolect.json");
    }

    // ----------------------------------------------------------------- drafting

    [Fact]
    public void A_guide_reading_transcripts_may_carry_only_checks_about_words()
    {
        var words = Idiolect();
        VoiceGuideDrafting.Problem(words).Should().BeNull();

        var form = Idiolect();
        form.CheckTypes.Add(VoiceChecker.HeadingCase);
        VoiceGuideDrafting.Problem(form).Should().Contain("heading-case");

        var withoutTranscripts = Idiolect(1, "mark");
        withoutTranscripts.CheckTypes.Add(VoiceChecker.HeadingCase);
        VoiceGuideDrafting.Problem(withoutTranscripts).Should().BeNull();

        VoiceGuideDrafting.Unreadable(Idiolect()).Should().BeEmpty("transcripts are a source Loadout can read");
    }

    [Fact]
    public void Mining_is_told_what_the_guide_learns_and_offered_only_its_checks()
    {
        var prompt = VoiceMining.Prompt(Idiolect(), [], []);

        prompt.Should().Contain("The words and phrases you reach for.");
        prompt.Should().Contain("avoid-phrase").And.Contain("prefer-spelling");
        prompt.Should().NotContain("heading-case").And.NotContain("commit-subject");

        VoiceMining.Prompt(VoiceGuides.Prose(), [], []).Should().Contain("heading-case").And.Contain("commit-subject");
    }

    // -------------------------------------------------------------------- store

    [Fact]
    public async Task A_guide_is_found_by_id_for_its_newest_version_or_by_id_and_version()
    {
        (await _store.SaveGuideAsync(Idiolect(1))).Failed.Should().BeFalse();
        (await _store.SaveGuideAsync(Idiolect(2))).Failed.Should().BeFalse();

        (await _store.FindGuideAsync("prose")).Value!.Reference.Should().Be("prose/1");
        (await _store.FindGuideAsync("Idiolect")).Value!.Reference.Should().Be("idiolect/2");
        (await _store.FindGuideAsync("idiolect/1")).Value!.Reference.Should().Be("idiolect/1");

        var missing = await _store.FindGuideAsync("nothing");
        missing.Failed.Should().BeTrue();
        missing.Error.Should().Contain("prose").And.Contain("idiolect");

        (await _store.FindGuideAsync("idiolect/3")).Failed.Should().BeTrue();
        (await _store.FindGuideAsync("idiolect/two")).Failed.Should().BeTrue();
    }

    [Fact]
    public async Task The_current_guides_are_those_that_ship_then_the_newest_of_each_drafted_one()
    {
        await _store.SaveGuideAsync(Idiolect(1));
        await _store.SaveGuideAsync(Idiolect(2));

        var current = await _store.CurrentGuidesAsync();

        current.Value!.Select(g => g.Reference).Should().Equal("prose/1", "time/1", "idiolect/2");
    }

    private async Task LearnedAsync()
    {
        await _store.SaveGuideAsync(Idiolect());

        await VoiceStore.WriteProfileAsync(
            _store.PathOf(StyleLayer.Personal, CodingStyles.Personal, "prose", null)!,
            new VoiceProfile
            {
                Guide = "prose/1",
                Rules = [new VoiceRule { Id = "short-sentences", Statement = "Keep sentences short." }],
            });

        await VoiceStore.WriteProfileAsync(
            _store.PathOf(StyleLayer.Personal, CodingStyles.Personal, "idiolect", null)!,
            new VoiceProfile
            {
                Guide = "idiolect/1",
                Rules =
                [
                    new VoiceRule
                    {
                        Id = "british",
                        Statement = "Spell it colour.",
                        Check = new VoiceCheck
                        {
                            Type = VoiceChecker.PreferSpelling,
                            Spellings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["color"] = "colour" },
                        },
                    },
                ],
            });
    }

    [Fact]
    public async Task Every_guides_rules_are_rendered_into_the_one_voice_file()
    {
        await LearnedAsync();

        var rendered = await _store.RenderAsync(StyleLayer.Personal, CodingStyles.Personal, null);

        rendered.Failed.Should().BeFalse(rendered.Error);
        var text = await File.ReadAllTextAsync(rendered.Value!.Path);
        text.Should().Contain("- Keep sentences short.").And.Contain("- Spell it colour.");
        text.Should().Contain("(prose/1, idiolect/1)");
        VoiceRendering.IsHandEdited(text).Should().BeFalse();
    }

    [Fact]
    public async Task Writing_is_checked_against_every_guides_rules()
    {
        await LearnedAsync();

        var rules = await _store.RulesInForceAsync(null, null);

        rules.Value!.Select(r => r.Rule.Id).Should().Equal("short-sentences", "british");
        VoiceChecker.Check("The color is red.", VoiceGuides.Document, [.. rules.Value!.Select(r => r.Rule)])
            .Findings.Should().ContainSingle().Which.Rule.Should().Be("british");
    }

    /// <summary>A person who is never asked anything, for dry runs.</summary>
    private sealed class NoPrompts : IVoiceLearnPrompts
    {
        public void Progress(string message)
        {
        }

        public VoiceCapAnswer Cap(VoiceCapQuestion question, VoiceEstimate estimate, VoiceCap proposed, VoiceCap? stored) =>
            throw new InvalidOperationException("A dry run asks nothing.");

        public bool PastCap(VoiceCap cap, int calls, decimal spent, int remaining) =>
            throw new InvalidOperationException("A dry run asks nothing.");

        public VoiceReviewAnswer Review(VoiceReviewItem item) =>
            throw new InvalidOperationException("A dry run asks nothing.");
    }
}
