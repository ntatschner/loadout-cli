using FluentAssertions;
using Loadout.Core.Instructions;
using Loadout.Models.Instructions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// When the recall hook speaks, and when it says nothing.
/// </summary>
/// <remarks>
/// Silence is the property worth testing hardest. The hook runs before every
/// prompt, so one that answers an ordinary remark puts unrelated facts in front
/// of a session several times an hour — and a fact that arrives unasked reads
/// as though the launcher vouched for it.
/// </remarks>
public sealed class RecallHookTests
{
    [Fact]
    public void A_topic_reached_only_through_its_prose_is_not_worth_speaking_about()
    {
        // What "what did you have for breakfast" did to a real store: three
        // topics, every one of them matched on an ordinary word buried in a
        // body, none of them about anything that was asked.
        var matches = new[] { Match("annotated-tags-hide-the-commit", curated: false) };

        RecallHook.Worth(matches).Should().BeEmpty();
        RecallHook.Output(matches).Should().BeNull();
    }

    [Fact]
    public void A_topic_whose_name_or_description_was_reached_is_worth_speaking_about()
    {
        var matches = new[] { Match("winget-publishing-never-runs", curated: true) };

        RecallHook.Worth(matches).Should().ContainSingle()
            .Which.Topic.Name.Should().Be("winget-publishing-never-runs");
    }

    [Fact]
    public void A_word_half_the_store_declares_is_not_enough_to_interrupt_with()
    {
        // "release and i'll test" reached a topic about finding views in TUI
        // tests on the single word "test", which three topics declare.
        var matches = new[]
        {
            Match("tui-tests-must-find-views-by-property", curated: true, terms: 1, declaredBy: 3),
        };

        RecallHook.Worth(matches).Should().BeEmpty();
        RecallHook.Output(matches).Should().BeNull();
    }

    [Fact]
    public void One_word_is_enough_when_only_this_topic_declares_it()
    {
        // The case a count of words got wrong. "what did i need to get for
        // winget again" leaves one word once the ignored list has had it, and
        // the topic named for winget publishing is the only one declaring it —
        // which is as decisive as a match gets, not as weak.
        var matches = new[]
        {
            Match("winget-publishing-never-runs", curated: true, terms: 1, declaredBy: 1),
        };

        RecallHook.Worth(matches).Should().ContainSingle()
            .Which.Topic.Name.Should().Be("winget-publishing-never-runs");
    }

    [Fact]
    public void Two_topics_are_offered_and_not_more()
    {
        // Two because the ranking earns two. Measured against a real store the
        // best answer was first for most questions and second for the rest, so
        // one would sometimes be the wrong one; a third was a passenger.
        var matches = new[]
        {
            Match("first", curated: true),
            Match("second", curated: true),
            Match("third", curated: true),
        };

        RecallHook.Worth(matches).Should().HaveCount(2);
    }

    [Fact]
    public void The_prompt_is_read_out_of_what_the_agent_sends()
    {
        RecallHook.Parse("""{"hook_event_name":"UserPromptSubmit","prompt":"why did it skip"}""")
            .Should().Be("why did it skip");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("""{"prompt":""}""")]
    [InlineData("""{"something":"else"}""")]
    public void Anything_it_cannot_read_is_a_reason_to_stay_quiet(string? json) =>
        // It runs before every prompt. A payload shaped differently from what
        // this expects is a reason to say nothing, not to put an error in front
        // of somebody mid-sentence.
        RecallHook.Parse(json).Should().BeNull();

    private static MemoryMatch Match(string name, bool curated, int terms = 2, int declaredBy = 1) =>
        new(
            new MemoryTopic(
                name,
                $"memory/{name}.md",
                "what the topic is about",
                MemoryKind.Lesson,
                ["A fact worth carrying forward into the next session."],
                [],
                Bytes: 200,
                WrittenUtc: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            Score: 1.0,
            Matched: [],
            Terms: terms,
            Curated: curated,
            Sharpest: declaredBy);
}
