using System.Text.Json;
using FluentAssertions;
using Loadout.Core.Teams;
using Loadout.Core.Teams.Daemon;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What a run's call for help looks like when it arrives somewhere else.
/// </summary>
/// <remarks>
/// <para>
/// Five services, five shapes, and each one is a document somebody else's
/// server will reject if it is wrong. The useful thing to test is not that the
/// text is right — it is that the document is the shape that service reads,
/// because getting that wrong shows up as silence rather than an error.
/// </para>
/// <para>
/// Every one of them carries the link. A notice that says something is wrong
/// and leaves you to find it is half a notice.
/// </para>
/// </remarks>
public sealed class NotifierTests
{
    private const string Title = "docs-crew needs you — it has stopped to ask";
    private const string Detail = "implementer/1 wants to use Bash for 'dotnet test'";
    private const string Link = "http://127.0.0.1:8477/studio/#teams/20261027-1059-75ed";

    private static JsonElement Sent(NoticeKind kind, string? chat = null) =>
        JsonDocument.Parse(Notifier.Body(kind, Title, Detail, Link, chat)).RootElement;

    [Fact]
    public void Slack_takes_a_bare_text()
    {
        var body = Sent(NoticeKind.Slack);

        body.GetProperty("text").GetString().Should().Contain(Title).And.Contain(Detail).And.Contain(Link);
    }

    [Fact]
    public void Discord_takes_content_rather_than_text()
    {
        // The whole difference between these two, and getting it wrong is a
        // 400 that looks like nothing happening.
        var body = Sent(NoticeKind.Discord);

        body.TryGetProperty("text", out _).Should().BeFalse();
        body.GetProperty("content").GetString().Should().Contain(Detail);
    }

    [Fact]
    public void Teams_takes_a_message_card()
    {
        var body = Sent(NoticeKind.Teams);

        body.GetProperty("@type").GetString().Should().Be("MessageCard");
        body.GetProperty("@context").GetString().Should().Be("https://schema.org/extensions");

        // What renders when a client understands none of the card. It carries
        // the whole message rather than a summary of it.
        body.GetProperty("text").GetString().Should().Contain(Detail).And.Contain(Link);
        body.GetProperty("summary").GetString().Should().Be(Title);
    }

    [Fact]
    public void Telegram_carries_the_chat_and_asks_for_no_markup()
    {
        var body = Sent(NoticeKind.Telegram, chat: "-100123");

        body.GetProperty("chat_id").GetString().Should().Be("-100123");
        body.GetProperty("text").GetString().Should().Contain(Detail);

        // No parse_mode at all: a node's own words can contain anything, and a
        // question arriving mangled - or rejected outright for unbalanced
        // markup - is worse than one arriving plain.
        body.TryGetProperty("parse_mode", out _).Should().BeFalse();
    }

    [Fact]
    public void Anything_else_gets_Loadouts_own_shape()
    {
        var body = Sent(NoticeKind.Generic);

        body.GetProperty("title").GetString().Should().Be(Title);
        body.GetProperty("detail").GetString().Should().Be(Detail);
        body.GetProperty("link").GetString().Should().Be(Link);
        body.TryGetProperty("at", out _).Should().BeTrue("a receiver wants to know when, not just what");
    }

    [Theory]
    [InlineData(NoticeKind.Slack)]
    [InlineData(NoticeKind.Discord)]
    [InlineData(NoticeKind.Teams)]
    [InlineData(NoticeKind.Telegram)]
    [InlineData(NoticeKind.Generic)]
    public void Every_shape_carries_the_link_back(NoticeKind kind)
    {
        Notifier.Body(kind, Title, Detail, Link, "1").Should().Contain(Link);
    }

    [Theory]
    [InlineData(NoticeKind.Slack)]
    [InlineData(NoticeKind.Discord)]
    [InlineData(NoticeKind.Teams)]
    [InlineData(NoticeKind.Telegram)]
    [InlineData(NoticeKind.Generic)]
    public void A_question_with_quotes_and_braces_in_it_still_makes_a_document(NoticeKind kind)
    {
        // A node's own words reach this, and they can contain anything. The
        // serialiser handles it; this is here so that stays true if anybody
        // ever builds one of these by hand.
        var awkward = "it wants Bash for 'git commit -m \"fix: {braces} & \\\"quotes\\\"\"'";

        var body = Notifier.Body(kind, Title, awkward, Link, "1");

        var act = () => JsonDocument.Parse(body);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(AttentionKind.Asking, "stopped to ask")]
    [InlineData(AttentionKind.GoingNowhere, "going nowhere")]
    [InlineData(AttentionKind.Quiet, "gone quiet")]
    [InlineData(AttentionKind.Spending, "spending too fast")]
    public void The_title_says_which_team_and_what_kind_of_trouble(AttentionKind kind, string expected)
    {
        // A phone showing "Loadout" and nothing else is a phone somebody stops
        // looking at.
        var run = new RunSummary(
            "20260918-1000-aaaa", "d", "docs-crew", "goal", "supervised",
            DateTimeOffset.UtcNow, null, null, 0m, 1, [], [], []);

        var title = Notifier.Title(run, new Attention(kind, "detail", "clears"));

        title.Should().StartWith("docs-crew needs you");
        title.Should().Contain(expected);
    }

    [Theory]
    [InlineData("slack", NoticeKind.Slack)]
    [InlineData("SLACK", NoticeKind.Slack)]
    [InlineData(" telegram ", NoticeKind.Telegram)]
    [InlineData("generic", NoticeKind.Generic)]
    public void The_configured_word_picks_the_shape(string word, NoticeKind expected)
    {
        Notices.KindOf(word).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("carrier pigeon")]
    public void Anything_unrecognised_sends_nowhere_rather_than_somewhere_wrong(string? word)
    {
        Notices.KindOf(word).Should().BeNull();
    }

    // A command that tries to end a code block early and to be read as markup.
    private const string Hostile = "echo ``` <b>&amp; done";

    private const string Plain = "implementer (builder) wants to list the build output. Nothing in its role allows that. Let it?";

    private static string Text(NoticeKind kind, string command = Hostile) =>
        JsonDocument.Parse(Notifier.Body(kind, Title, Plain, Link, "1", command)).RootElement
            .GetProperty(kind == NoticeKind.Discord ? "content" : "text").GetString()!;

    [Fact]
    public void Slack_puts_the_plain_line_first_and_the_command_in_a_block_it_cannot_close()
    {
        var text = Text(NoticeKind.Slack);

        text.IndexOf(Plain, StringComparison.Ordinal).Should().BeLessThan(text.IndexOf("```", StringComparison.Ordinal));

        // Exactly the opening and closing fence: the command's own three
        // backticks are broken so they cannot end the block.
        System.Text.RegularExpressions.Regex.Matches(text, "```").Count.Should().Be(2);

        // Slack reads &, < and > as its own, so they arrive escaped and show
        // as themselves.
        text.Should().Contain("&lt;b&gt;&amp;amp;");
        text.Should().NotContain("<b>");
    }

    [Fact]
    public void Discord_puts_the_plain_line_first_and_the_command_in_a_block_it_cannot_close()
    {
        var text = Text(NoticeKind.Discord);

        text.Should().StartWith(Title + "\n" + Plain + "\n```\n");
        System.Text.RegularExpressions.Regex.Matches(text, "```").Count.Should().Be(2);

        // No entities in Discord: the text is the text.
        text.Should().Contain("<b>&amp; done");
    }

    [Fact]
    public void Teams_puts_the_command_in_a_code_span_longer_than_any_backticks_inside_it()
    {
        var text = Text(NoticeKind.Teams);

        text.Should().StartWith(Plain + "\n\n");
        text.Should().Contain("````" + Hostile + "````");
    }

    [Fact]
    public void Telegram_escapes_everything_before_asking_for_HTML()
    {
        var body = JsonDocument.Parse(Notifier.Body(NoticeKind.Telegram, Title, Plain, Link, "1", Hostile)).RootElement;
        var text = body.GetProperty("text").GetString()!;

        body.GetProperty("parse_mode").GetString().Should().Be("HTML");
        text.Should().Contain("<pre>echo ``` &lt;b&gt;&amp;amp; done</pre>");

        // The only tags in it are the ones put there.
        System.Text.RegularExpressions.Regex.Matches(text, "<[a-z/]").Count.Should().Be(2);
        text.IndexOf(Plain, StringComparison.Ordinal).Should().BeLessThan(text.IndexOf("<pre>", StringComparison.Ordinal));
    }

    [Fact]
    public void The_generic_shape_carries_the_command_as_its_own_field()
    {
        var body = JsonDocument.Parse(Notifier.Body(NoticeKind.Generic, Title, Plain, Link, null, Hostile)).RootElement;

        body.GetProperty("detail").GetString().Should().Be(Plain);
        body.GetProperty("command").GetString().Should().Be(Hostile);
    }

    [Theory]
    [InlineData(NoticeKind.Slack)]
    [InlineData(NoticeKind.Discord)]
    [InlineData(NoticeKind.Teams)]
    [InlineData(NoticeKind.Telegram)]
    public void A_long_command_is_cut_and_says_where_the_rest_is(NoticeKind kind)
    {
        var command = new string('x', 2000);
        var text = Text(kind, command);

        text.Should().NotContain(command);
        text.Should().Contain(new string('x', Notifier.CommandLimit) + "…");
        text.Should().Contain(Notifier.CutShort);
    }

    [Fact]
    public void A_waiting_permission_is_spoken_as_words_then_its_command()
    {
        var gate = new PendingAsk(
            "implementer-1", "implementer", "builder", "Bash", "ls build && rm -rf build", DateTimeOffset.UtcNow,
            Said: "list the build output", Blocked: "rm -rf build", Flags: [AskWording.Deletes]);
        var run = new RunSummary(
            "20260918-1000-aaaa", "d", "docs-crew", "goal", "supervised",
            DateTimeOffset.UtcNow, null, null, 0m, 1, [], [], [], Gates: [gate]);

        var (detail, command) = Notices.Spoken(run, new Attention(AttentionKind.Asking, gate.Asking, "you answer it", gate.Id));

        detail.Should().Be(
            "implementer (builder) wants to list the build output. Its role does not allow 'rm -rf build'. "
            + "Careful: the command deletes files. Let it?");
        command.Should().Be("ls build && rm -rf build");
    }
}
