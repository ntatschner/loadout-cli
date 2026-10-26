using System.Text.Json;

namespace Loadout.Core.Teams.Daemon;

/// <summary>Where a notice is being sent, which decides its shape.</summary>
public enum NoticeKind
{
    /// <summary>A Slack incoming webhook.</summary>
    Slack,

    /// <summary>A Discord webhook.</summary>
    Discord,

    /// <summary>A Microsoft Teams incoming webhook.</summary>
    Teams,

    /// <summary>The Telegram bot API's sendMessage.</summary>
    Telegram,

    /// <summary>Somebody else's endpoint, given Loadout's own shape.</summary>
    Generic,
}

/// <summary>
/// Saying out loud, somewhere else, that a run wants somebody.
/// </summary>
/// <remarks>
/// <para>
/// The browser tells you while you are looking at it, and the whole point of
/// unattended runs is that you are not. This is the channel that reaches
/// somebody whose browser is shut.
/// </para>
/// <para>
/// Every one of these is "POST JSON to a URL" and differs only in what the
/// JSON looks like, so there is one mechanism and a shape per service. None of
/// them is a client library: a webhook is one request, and a dependency for
/// one request is a dependency to carry for ever.
/// </para>
/// <para>
/// Every notice carries a link back to the run. A notification that tells you
/// something is wrong and leaves you to find it is half a notification, which
/// is why LangSmith's alerts carry a "View Runs" button and Langfuse's
/// webhooks carry a permalink.
/// </para>
/// </remarks>
public static class Notifier
{
    /// <summary>
    /// The JSON body for one notice, in the shape the service expects.
    /// </summary>
    /// <param name="kind">Where it is going.</param>
    /// <param name="title">One line: which run, and that it wants somebody.</param>
    /// <param name="detail">What it wants, in its own words.</param>
    /// <param name="link">Where to go to deal with it.</param>
    /// <param name="chat">The Telegram chat, ignored by everything else.</param>
    /// <param name="command">
    /// The command a permission is about, set apart from the words in the
    /// form each service shows code in. Null sends the words alone.
    /// </param>
    public static string Body(
        NoticeKind kind,
        string title,
        string detail,
        string link,
        string? chat = null,
        string? command = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        if (command is { Length: > 0 })
        {
            return Fenced(kind, title, detail, link, chat, command);
        }

        var plain = $"{title}\n{detail}\n{link}";

        return kind switch
        {
            // Slack and Discord both take a bare "text"/"content", and both
            // render a bare URL as a link, so nothing here builds markup.
            NoticeKind.Slack => JsonSerializer.Serialize(new { text = plain }),

            NoticeKind.Discord => JsonSerializer.Serialize(new { content = plain }),

            // Teams' old-style connector card. "text" is what renders when a
            // client does not understand the card at all, so it carries the
            // whole message rather than a summary of it.
            NoticeKind.Teams => JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["@type"] = "MessageCard",
                ["@context"] = "https://schema.org/extensions",
                ["summary"] = title,
                ["title"] = title,
                ["text"] = $"{detail}\n\n{link}",
            }),

            NoticeKind.Telegram => JsonSerializer.Serialize(new
            {
                chat_id = chat ?? string.Empty,
                text = plain,

                // Deliberately not parsed as markup: a node's own words can
                // contain anything at all, and a run's question arriving
                // mangled - or rejected by the API for unbalanced markup - is
                // worse than a run's question arriving plain.
                disable_web_page_preview = true,
            }),

            _ => JsonSerializer.Serialize(new
            {
                title,
                detail,
                link,
                at = DateTimeOffset.UtcNow,
            }),
        };
    }

    /// <summary>The longest command a notice carries before it is cut.</summary>
    public const int CommandLimit = 400;

    /// <summary>What a notice says under a command it had to cut.</summary>
    public const string CutShort = "The whole command is on the dashboard.";

    /*
      A notice with a command in it: the words, then the command as code.

      The command is the supervised agent's and can hold anything, including
      whatever ends a code block early. So each service gets it in the one form
      that service cannot read as anything but text: a fence with its own
      backticks broken for Slack and Discord, a code span longer than any run
      of backticks inside it for Teams, and escaped HTML in a <pre> for
      Telegram - the only one asked to parse markup at all, and only here,
      where every character it is sent has been escaped first. Slack reads &,
      < and > as its own, so they are escaped everywhere in its text.

      A long command is cut, with a line saying where the rest is, so a
      two-thousand-character command does not bury the line above it.
    */
    private static string Fenced(NoticeKind kind, string title, string detail, string link, string? chat, string command)
    {
        var cut = command.Length > CommandLimit;
        var shown = cut ? command[..CommandLimit] + "…" : command;
        var after = cut ? $"{CutShort}\n{link}" : link;

        return kind switch
        {
            NoticeKind.Slack => JsonSerializer.Serialize(new
            {
                text = Slack($"{title}\n{detail}\n") + "```\n" + Slack(Unfenced(shown)) + "\n```\n" + Slack(after),
            }),

            NoticeKind.Discord => JsonSerializer.Serialize(new
            {
                content = $"{title}\n{detail}\n```\n{Unfenced(shown)}\n```\n{after}",
            }),

            NoticeKind.Teams => JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["@type"] = "MessageCard",
                ["@context"] = "https://schema.org/extensions",
                ["summary"] = title,
                ["title"] = title,
                ["text"] = $"{detail}\n\n{Span(shown)}\n\n{after}",
            }),

            NoticeKind.Telegram => JsonSerializer.Serialize(new
            {
                chat_id = chat ?? string.Empty,
                text = $"{Html(title)}\n{Html(detail)}\n<pre>{Html(shown)}</pre>\n{Html(after)}",
                parse_mode = "HTML",
                disable_web_page_preview = true,
            }),

            _ => JsonSerializer.Serialize(new
            {
                title,
                detail,
                command,
                link,
                at = DateTimeOffset.UtcNow,
            }),
        };
    }

    // Three backticks inside a fence end it. A zero-width space between them
    // keeps them three backticks to whoever reads it and nothing to the fence.
    private static string Unfenced(string text) =>
        text.Replace("```", "`​`​`", StringComparison.Ordinal);

    // A code span is closed only by a run of backticks as long as the one that
    // opened it, so it opens with one longer than any inside.
    private static string Span(string text)
    {
        var longest = 0;
        var run = 0;

        foreach (var c in text)
        {
            run = c == '`' ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }

        var ticks = new string('`', longest + 1);
        var pad = text.StartsWith('`') || text.EndsWith('`') ? " " : string.Empty;

        return $"{ticks}{pad}{text}{pad}{ticks}";
    }

    private static string Slack(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    private static string Html(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    /// <summary>
    /// The line that says which run and what it wants.
    /// </summary>
    /// <remarks>
    /// The team and the reason, because a phone showing "Loadout" and nothing
    /// else is a phone somebody stops looking at.
    /// </remarks>
    public static string Title(RunSummary run, Attention reason) =>
        $"{run.Team} needs you — {Word(reason.Kind)}";

    private static string Word(AttentionKind kind) => kind switch
    {
        AttentionKind.Asking => "it has stopped to ask",
        AttentionKind.GoingNowhere => "it is going nowhere",
        AttentionKind.Quiet => "a node has gone quiet",
        _ => "it is spending too fast",
    };
}
