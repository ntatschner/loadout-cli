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
    public static string Body(
        NoticeKind kind,
        string title,
        string detail,
        string link,
        string? chat = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

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
