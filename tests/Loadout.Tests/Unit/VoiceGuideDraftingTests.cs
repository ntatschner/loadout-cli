using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Loadout.Agents.Voice;
using Loadout.Core.Voice;
using Loadout.Models.Voice;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The meta step: a guide drafted by a model, read strictly, and saved under a
/// version that is never replaced.
/// </summary>
public sealed class VoiceGuideDraftingTests : IDisposable
{
    private readonly TemporaryWorkspace _space = new();
    private readonly VoiceStore _store;

    public VoiceGuideDraftingTests()
    {
        _store = new VoiceStore(_space.Paths.Paths.Config, _space.Paths.Paths.State, _space.Workspace.LocalPath);
    }

    public void Dispose() => _space.Dispose();

    /// <summary>A sound answer, which each test breaks in the one way it is about.</summary>
    private static JsonObject Answer() => JsonNode.Parse(JsonSerializer.Serialize(new
    {
        contract = "voice-guide/1",
        summary = "The words and phrases you reach for.",
        sources = new[] { "mark", "interview" },
        surfaces = new[] { "doc", "commit" },
        checkTypes = new[] { "avoid-phrase" },
        evidence = new { citations = 2, sources = 1 },
        promotion = new { agreement = 0.9, consecutiveRuns = 2, editFloor = 5, demotionFailures = 2, unconfirmedLapses = 3 },
        cap = new { margin = 0.25, lowerBelow = 0.5, seedCallUsd = 0.3, batchCharacters = 40000 },
        scoring = new { holdOut = 0.2 },
    }))!.AsObject();

    private static string Text(JsonObject answer) => answer.ToJsonString();

    private VoiceGuideDrafter Drafter(ScriptedDetachedLauncher launcher, VoiceStore? store = null) =>
        new(store ?? _store, launcher, _space.Paths, TimeProvider.System);

    // ------------------------------------------------------------- reading

    [Fact]
    public void The_id_and_version_are_set_by_loadout_never_by_the_model()
    {
        var answer = Answer();
        answer["id"] = "prose";
        answer["version"] = 7;

        var guide = VoiceGuideDrafting.Read(Text(answer), "idiolect", 2);

        guide.Failed.Should().BeFalse(guide.Error);
        guide.Value!.Reference.Should().Be("idiolect/2");
        guide.Value.Evidence.Citations.Should().Be(2);
    }

    [Fact]
    public void A_check_loadout_cannot_run_is_refused_and_the_ones_it_can_are_named()
    {
        var answer = Answer();
        answer["checkTypes"] = new JsonArray("avoid-phrase", "passive-ratio");

        var read = VoiceGuideDrafting.Read(Text(answer), "idiolect", 1);

        read.Failed.Should().BeTrue();
        read.Error.Should().Contain("passive-ratio").And.Contain("prefer-spelling");
    }

    [Theory]
    [InlineData("promotion", "agreement", 1.5)]
    [InlineData("promotion", "consecutiveRuns", 0)]
    [InlineData("promotion", "editFloor", -1)]
    [InlineData("evidence", "sources", 3)]
    [InlineData("evidence", "citations", 0)]
    [InlineData("cap", "margin", 3)]
    [InlineData("cap", "lowerBelow", 1)]
    [InlineData("cap", "seedCallUsd", 0)]
    [InlineData("cap", "batchCharacters", 10)]
    [InlineData("scoring", "holdOut", 0.8)]
    public void A_threshold_out_of_its_range_is_refused_rather_than_clamped(string section, string field, double value)
    {
        var answer = Answer();
        answer[section]![field] = value;

        VoiceGuideDrafting.Read(Text(answer), "idiolect", 1).Failed.Should().BeTrue();
    }

    [Fact]
    public void A_source_loadout_cannot_read_yet_is_kept_and_said()
    {
        var answer = Answer();
        answer["sources"] = new JsonArray("mark", "code");

        var guide = VoiceGuideDrafting.Read(Text(answer), "architecture", 1).Value!;

        guide.Sources.Should().Equal("mark", "code");
        VoiceGuideDrafting.Unreadable(guide).Should().Equal("code");
        VoiceGuideDrafting.Unreadable(VoiceGuides.Prose()).Should().BeEmpty("prose/1 learns only from what Loadout gathers");
    }

    [Theory]
    [InlineData("sources", "Source Code")]
    [InlineData("surfaces", "Read Me")]
    public void A_source_or_surface_that_is_not_a_short_word_is_refused(string field, string value)
    {
        var answer = Answer();
        answer[field] = new JsonArray("mark", value);

        VoiceGuideDrafting.Read(Text(answer), "idiolect", 1).Failed.Should().BeTrue();
    }

    [Fact]
    public void An_answer_in_another_shape_is_refused()
    {
        var answer = Answer();
        answer["contract"] = "voice-mine/1";

        VoiceGuideDrafting.Read(Text(answer), "idiolect", 1).Failed.Should().BeTrue();
        VoiceGuideDrafting.Read("", "idiolect", 1).Failed.Should().BeTrue();
    }

    [Fact]
    public void A_shipped_guide_or_an_unusable_id_cannot_be_drafted()
    {
        VoiceGuideDrafting.Refusal("prose").Should().Contain("ships with Loadout");
        VoiceGuideDrafting.Refusal("My Guide").Should().NotBeNull();
        VoiceGuideDrafting.Refusal("idiolect").Should().BeNull();
    }

    [Fact]
    public void The_prompt_offers_every_check_and_source_and_leaves_the_id_to_loadout()
    {
        var prompt = VoiceGuideDrafting.Prompt("idiolect", "the phrases I reach for");

        prompt.Should().Contain("'idiolect'").And.Contain("the phrases I reach for");

        foreach (var type in VoiceChecker.Types)
        {
            prompt.Should().Contain($"- {type}:");
        }

        foreach (var source in VoiceGuideDrafting.KnownSources.Keys)
        {
            prompt.Should().Contain($"- {source}:");
        }

        prompt.Should().Contain("Leave out the id and version");
    }

    // ------------------------------------------------------------- storage

    [Fact]
    public async Task A_saved_version_is_never_replaced()
    {
        var guide = VoiceGuideDrafting.Read(Text(Answer()), "idiolect", 1).Value!;

        (await _store.SaveGuideAsync(guide)).Succeeded.Should().BeTrue();

        guide.Summary = "Something else.";
        var again = await _store.SaveGuideAsync(guide);

        again.Failed.Should().BeTrue();
        again.Error.Should().Contain("never replaced");

        var saved = (await _store.SavedGuidesAsync()).Value!.Single();
        saved.Summary.Should().Be("The words and phrases you reach for.");
    }

    [Fact]
    public async Task A_saved_guide_whose_contents_disagree_with_where_it_is_kept_is_refused()
    {
        var guide = VoiceGuideDrafting.Read(Text(Answer()), "idiolect", 1).Value!;
        (await _store.SaveGuideAsync(guide)).Succeeded.Should().BeTrue();

        var path = Path.Combine(_store.GuidesDirectory!, "idiolect", "1.json");
        await File.WriteAllTextAsync(path, (await File.ReadAllTextAsync(path)).Replace("\"version\": 1", "\"version\": 2", StringComparison.Ordinal));

        var read = await _store.SavedGuidesAsync();

        read.Failed.Should().BeTrue();
        read.Error.Should().Contain("never edited");
    }

    // ------------------------------------------------------------- drafting

    [Fact]
    public async Task Drafting_again_proposes_the_next_version_and_saves_nothing()
    {
        (await _store.SaveGuideAsync(VoiceGuideDrafting.Read(Text(Answer()), "idiolect", 1).Value!)).Succeeded.Should().BeTrue();

        var launcher = new ScriptedDetachedLauncher(ScriptedDetachedLauncher.Result(Text(Answer())));

        var drafted = await Drafter(launcher).DraftAsync(new VoiceGuideDraftRequest("Idiolect", "the phrases I reach for"));

        drafted.Failed.Should().BeFalse(drafted.Error);
        drafted.Value!.Guide.Reference.Should().Be("idiolect/2");
        drafted.Value.CostUsd.Should().Be(0.05m);
        launcher.Options!.OutputSchemaJson.Should().Be(VoiceGuideDrafting.Schema);
        launcher.Options.AllowedTools.Should().BeEmpty("drafting reads nothing and writes nothing");

        (await _store.SavedGuidesAsync()).Value!.Should().ContainSingle("the person saves a draft, not the drafter");
    }

    [Fact]
    public async Task Nothing_is_spent_on_a_guide_that_could_not_be_saved()
    {
        var launcher = new ScriptedDetachedLauncher(ScriptedDetachedLauncher.Result(Text(Answer())));

        (await Drafter(launcher).DraftAsync(new VoiceGuideDraftRequest("prose", "anything"))).Failed.Should().BeTrue();
        (await Drafter(launcher).DraftAsync(new VoiceGuideDraftRequest("idiolect", "  "))).Failed.Should().BeTrue();

        var homeless = new VoiceStore(_space.Paths.Paths.Config, _space.Paths.Paths.State, workspaceRoot: null);
        (await Drafter(launcher, homeless).DraftAsync(new VoiceGuideDraftRequest("idiolect", "the phrases"))).Failed.Should().BeTrue();

        launcher.Started.Should().Be(0);
    }

    [Fact]
    public async Task A_draft_the_engine_could_not_honour_fails_after_one_retry()
    {
        var bad = Answer();
        bad["checkTypes"] = new JsonArray("passive-ratio");

        // The same unusable answer to the question and to being asked again.
        var launcher = new ScriptedDetachedLauncher(
            ScriptedDetachedLauncher.Result(Text(bad)), ScriptedDetachedLauncher.Result(Text(bad)));

        var drafted = await Drafter(launcher).DraftAsync(new VoiceGuideDraftRequest("idiolect", "the phrases"));

        drafted.Failed.Should().BeTrue();
        drafted.Error.Should().Contain("passive-ratio");
        launcher.Pipe!.Written.ToString().Should().Contain("That answer was not usable", "it was asked once more");
    }
}
