using FluentAssertions;
using Loadout.Core.Instructions;
using Loadout.Models.Instructions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Which memory topics a question reaches.
/// </summary>
/// <remarks>
/// <para>
/// The store here is a small copy of a real one, slugs and all, because the
/// cases that matter are about how real topics are written: names are
/// hyphenated, the fact that answers a question rarely repeats its words, and
/// several topics say "the build" without being about the build.
/// </para>
/// <para>
/// What it cannot do is asserted as firmly as what it can. A search that matches
/// words and is described as matching meanings would send somebody away
/// believing a fact is unrecorded.
/// </para>
/// </remarks>
public sealed class MemorySearchTests
{
    private static readonly IReadOnlyList<MemoryTopic> Store =
    [
        Topic(
            "windows-restart-manager-disabled",
            "why installers fail with 1603 over a running app",
            ["The MSI Restart Manager is disabled by policy, so an upgrade over a running launcher fails."]),
        Topic(
            "build-quirks",
            "things that surprise people about the build",
            ["The first build after a clean takes four minutes; the analyzers warm up."]),
        Topic(
            "release-process",
            "how a release is cut and what the build does then",
            ["The build signs the installer before it is published."]),
        Topic(
            "palette-accepted-event",
            "the command palette list does not raise Accepted",
            ["Subscribe to Accepting instead; Accepted never fires on the palette's list."]),
    ];

    [Fact]
    public void A_hyphenated_topic_name_is_found_by_the_words_in_it()
    {
        // Nobody types the slug. They type two of its words, which only matches
        // if the name was taken apart the same way the question was.
        Rank("restart manager").Should().ContainSingle()
            .Which.Topic.Name.Should().Be("windows-restart-manager-disabled");
    }

    [Fact]
    public void The_rare_word_decides_and_not_the_repeated_one()
    {
        // "build" is in two topics and separates little; "signs" is in one and
        // is the whole question. build-quirks says "build" in its name, its
        // description and its fact, and must still lose: repetition of a common
        // word is not relevance, which is what saturating the weight is for.
        Rank("build signs").First().Topic.Name.Should().Be("release-process");
    }

    [Fact]
    public void The_facts_that_carried_the_words_come_back_with_the_match()
    {
        var match = Rank("analyzers").Should().ContainSingle().Subject;

        // So the reader can see what was matched rather than trust the ranking.
        match.Matched.Should().ContainSingle()
            .Which.Should().Contain("analyzers warm up");
    }

    [Fact]
    public void A_match_on_the_name_alone_still_comes_back()
    {
        // "event" is in the name and nowhere else in that topic — not in its
        // description, not in its fact. "palette" would not do: the fact says
        // "the palette's list", so it would come back matched and prove nothing.
        var match = Rank("event").Should().ContainSingle().Subject;

        match.Topic.Name.Should().Be("palette-accepted-event");
        match.Matched.Should().BeEmpty("nothing in the facts carried the word");
    }

    [Fact]
    public void A_question_made_only_of_ordinary_words_returns_nothing()
    {
        // Not everything. A search asked nothing that answered everything would
        // look like a search that matched everything.
        Rank("the and of it").Should().BeEmpty();
    }

    [Fact]
    public void Words_it_has_never_seen_return_nothing_rather_than_a_guess()
    {
        Rank("kubernetes ingress").Should().BeEmpty();
    }

    [Fact]
    public void It_matches_words_and_not_meanings()
    {
        // The limitation, asserted so it stays true or stays known. "Installer"
        // is in the store; "setup program" means the same and is not.
        Rank("installer").Should().NotBeEmpty();
        Rank("setup program").Should().BeEmpty();
    }

    [Fact]
    public void A_term_everything_mentions_loses_to_one_almost_nothing_does()
    {
        // The rare term has to be weak and the common one strong, or the topic
        // carrying both wins for reasons that have nothing to do with rarity.
        // So: nine topics leaning hard on "build", and one that mentions
        // "quirks" exactly once, in a fact, under a name that does not repeat
        // it. Saturation cannot save that topic — one mention stays one
        // mention. Only the fact that nine other topics say "build" and none of
        // them says "quirks" puts it top.
        var store = Enumerable.Range(1, 9)
            .Select(i => Topic($"build-stage-{i}", "the build", ["The build runs."]))
            .Append(Topic("odd-corner", "an odd corner of the system", ["Quirks."]))
            .ToList();

        MemorySearch.Rank(store, "build quirks").First()
            .Topic.Name.Should().Be("odd-corner");
    }

    [Fact]
    public void No_more_than_the_limit_comes_back()
    {
        // Four topics match, so the limit has something to cut. A query matching
        // only two would pass with no limit applied at all.
        Rank("build palette restart", limit: 5).Should().HaveCount(4);
        Rank("build palette restart", limit: 2).Should().HaveCount(2);
    }

    [Fact]
    public void An_empty_question_or_an_empty_store_is_answered_with_nothing()
    {
        MemorySearch.Rank(Store, null).Should().BeEmpty();
        MemorySearch.Rank(Store, "   ").Should().BeEmpty();
        MemorySearch.Rank([], "build").Should().BeEmpty();
    }

    private static IReadOnlyList<MemoryMatch> Rank(string query, int limit = 5) =>
        MemorySearch.Rank(Store, query, limit);

    private static MemoryTopic Topic(string name, string description, string[] facts) =>
        new(
            name,
            $"memory/{name}.md",
            description,
            MemoryKind.Lesson,
            facts,
            [],
            Bytes: 200,
            WrittenUtc: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
}
