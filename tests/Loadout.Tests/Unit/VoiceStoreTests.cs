using FluentAssertions;
using Loadout.Core.Instructions;
using Loadout.Core.Voice;
using Loadout.Models.Voice;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

public sealed class VoiceStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "loadout-voice-" + Guid.NewGuid().ToString("N"));

    public VoiceStoreTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private VoiceStore Store() =>
        new(Path.Combine(_root, "config"), Path.Combine(_root, "state"), Path.Combine(_root, "workspace"));

    private static VoiceProfile Profile(params (string Id, string Statement)[] rules) => new()
    {
        Guide = "prose/1",
        Rules = [.. rules.Select(r => new VoiceRule { Id = r.Id, Statement = r.Statement })],
    };

    [Fact]
    public async Task A_more_specific_layer_replaces_a_rule_by_id_and_keeps_the_rest()
    {
        var store = Store();

        await VoiceStore.WriteProfileAsync(
            store.PathOf(StyleLayer.Personal, "personal", "prose", null)!,
            Profile(("british", "mine"), ("short", "mine")));
        await VoiceStore.WriteProfileAsync(
            store.PathOf(StyleLayer.Named, "work", "prose", null)!,
            Profile(("british", "work's")));
        await VoiceStore.WriteProfileAsync(
            store.PathOf(StyleLayer.Codebase, "codebase", "prose", "demo")!,
            Profile(("commits", "the codebase's")));

        var layers = await store.LayersAsync("prose", "work", "demo");

        var rules = VoiceStore.Resolve(layers.Value!);

        rules.Select(r => (r.Rule.Id, r.Rule.Statement, r.Layer)).Should().Equal(
            ("british", "work's", StyleLayer.Named),
            ("short", "mine", StyleLayer.Personal),
            ("commits", "the codebase's", StyleLayer.Codebase));
    }

    [Fact]
    public async Task A_named_style_not_chosen_is_not_in_force()
    {
        var store = Store();

        await VoiceStore.WriteProfileAsync(
            store.PathOf(StyleLayer.Named, "work", "prose", null)!, Profile(("british", "work's")));

        var layers = await store.LayersAsync("prose", chosen: null, slug: "demo");

        VoiceStore.Resolve(layers.Value!).Should().BeEmpty();
        layers.Value!.Select(l => l.Layer).Should().Equal(StyleLayer.Personal, StyleLayer.Codebase);
    }

    [Fact]
    public async Task A_profile_survives_a_round_trip_with_its_check_and_citations()
    {
        var path = Path.Combine(_root, "p.json");

        var profile = Profile(("british", "Write British spellings."));
        profile.Rules[0].Check = new VoiceCheck
        {
            Type = VoiceChecker.PreferSpelling,
            Spellings = new() { ["color"] = "colour" },
        };
        profile.Rules[0].Citations.Add(new VoiceCitation { Source = "mark", Reference = "docs/a.md", Excerpt = "colour", Hash = "ab" });

        (await VoiceStore.WriteProfileAsync(path, profile)).Succeeded.Should().BeTrue();

        var read = (await VoiceStore.ReadProfileAsync(path)).Value!;

        read.Rules.Should().ContainSingle().Which.Check!.Spellings.Should().ContainKey("color");
        read.Rules[0].Citations.Should().ContainSingle().Which.Hash.Should().Be("ab");
    }

    [Fact]
    public async Task A_damaged_profile_is_refused_rather_than_read_as_empty()
    {
        var path = Path.Combine(_root, "bad.json");
        await File.WriteAllTextAsync(path, "{ not json");

        (await VoiceStore.ReadProfileAsync(path)).Failed.Should().BeTrue();
    }

    [Fact]
    public void The_hash_ignores_line_endings_and_nothing_else()
    {
        VoiceStore.Hash("a\r\nb").Should().Be(VoiceStore.Hash("a\nb"));
        VoiceStore.Hash("a\nb").Should().NotBe(VoiceStore.Hash("a\nb "));
    }

    [Fact]
    public async Task A_file_mark_lapses_when_its_text_changes_and_holds_again_once_reconfirmed()
    {
        var store = Store();
        var marks = new VoiceMarks(new FakeGit(_root), new FakePullRequests(), store, TimeProvider.System);
        var repository = Path.Combine(_root, "repo");
        Directory.CreateDirectory(Path.Combine(repository, "docs"));
        var file = Path.Combine(repository, "docs", "guide.md");
        await File.WriteAllTextAsync(file, "Written by hand.");

        var candidates = (await marks.CandidatesAsync(repository, "docs/guide.md")).Value!;
        candidates.Should().ContainSingle().Which.Reference.Should().Be("docs/guide.md");

        (await marks.MarkAsync(repository, candidates)).Value.Should().Be((1, 0));
        (await marks.StandingAsync(repository)).Value!.Single().State.Should().Be(VoiceMarkState.Holds);

        await File.WriteAllTextAsync(file, "Written by hand, then edited.");

        (await marks.StandingAsync(repository)).Value!.Single().State.Should().Be(VoiceMarkState.Lapsed);

        var lapsed = (await marks.LapsedAsync(repository)).Value!;
        lapsed.Should().ContainSingle();

        (await marks.MarkAsync(repository, lapsed)).Value.Should().Be((0, 1), "re-confirming is not a new mark");
        (await marks.StandingAsync(repository)).Value!.Single().State.Should().Be(VoiceMarkState.Holds);

        File.Delete(file);

        (await marks.StandingAsync(repository)).Value!.Single().State.Should().Be(VoiceMarkState.Missing);
    }

    [Fact]
    public async Task A_file_outside_the_repository_cannot_be_marked_from_it()
    {
        var marks = new VoiceMarks(new FakeGit(_root), new FakePullRequests(), Store(), TimeProvider.System);
        var repository = Path.Combine(_root, "repo");
        Directory.CreateDirectory(repository);
        await File.WriteAllTextAsync(Path.Combine(_root, "elsewhere.md"), "x");

        (await marks.CandidatesAsync(repository, "../elsewhere.md")).Failed.Should().BeTrue();
    }

    [Fact]
    public async Task Unmarking_takes_the_mark_off_and_only_that_one()
    {
        var store = Store();
        var marks = new VoiceMarks(new FakeGit(_root), new FakePullRequests(), store, TimeProvider.System);
        var repository = Path.Combine(_root, "repo");
        Directory.CreateDirectory(repository);
        await File.WriteAllTextAsync(Path.Combine(repository, "a.md"), "a");
        await File.WriteAllTextAsync(Path.Combine(repository, "b.md"), "b");

        var a = (await marks.CandidatesAsync(repository, "a.md")).Value!;
        var b = (await marks.CandidatesAsync(repository, "b.md")).Value!;
        await marks.MarkAsync(repository, [.. a, .. b]);

        (await marks.UnmarkAsync(repository, a)).Value.Should().Be(1);

        (await marks.StandingAsync(repository)).Value!.Select(s => s.Mark.Reference).Should().Equal("b.md");
    }
}
