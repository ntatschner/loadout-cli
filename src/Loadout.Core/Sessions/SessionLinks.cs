using System.Text.Json;
using System.Text.RegularExpressions;

namespace Loadout.Core.Sessions;

/// <summary>
/// How a person's conversation is filed under the launch that started it.
/// </summary>
/// <remarks>
/// <para>
/// The agent chooses a conversation's id after it starts, so the launcher
/// cannot write it down itself. The launch puts its own id in the agent's
/// environment under <see cref="Variable"/>, and the agent's hook, run when
/// the conversation starts or resumes, hands both to
/// <c>loadout hook session-start</c>, which appends a link to the ledger.
/// </para>
/// <para>
/// Known rather than inferred. Before this, resuming guessed the launch from
/// the project, the agent and the time, and a confident wrong guess carried
/// another session's task over without a word. A conversation with no link
/// now carries nothing.
/// </para>
/// </remarks>
public static partial class SessionLinks
{
    /// <summary>The variable the launch's id is handed to the agent in.</summary>
    public const string Variable = "LOADOUT_LAUNCH_ID";

    /// <summary>The most of a hook's input read, which is plenty for an id and stops a runaway.</summary>
    public const int MaximumInput = 64 * 1024;

    /// <summary>Whether this is a launch id as the ledger mints them.</summary>
    public static bool IsLaunchId(string? id) => id is not null && LaunchId().IsMatch(id);

    /// <summary>Whether this could be an agent's own conversation id, and is safe on a command line.</summary>
    public static bool IsAgentSession(string? id) => id is not null && AgentSession().IsMatch(id);

    /// <summary>
    /// The <c>session_id</c> in a hook's input, or null for none or none readable.
    /// </summary>
    /// <remarks>
    /// The id alone, and nothing else in the input is kept: an agent's hook
    /// input can quote what it was about to run.
    /// </remarks>
    public static async Task<string?> ReadSessionIdAsync(TextReader? reader, CancellationToken ct = default)
    {
        if (reader is null)
        {
            return null;
        }

        var buffer = new char[MaximumInput];
        var read = 0;

        while (read < buffer.Length)
        {
            var got = await reader.ReadAsync(buffer.AsMemory(read), ct).ConfigureAwait(false);

            if (got == 0)
            {
                break;
            }

            read += got;
        }

        try
        {
            using var document = JsonDocument.Parse(buffer.AsMemory(0, read));

            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("session_id", out var session)
                && session.ValueKind == JsonValueKind.String
                ? session.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex LaunchId();

    [GeneratedRegex("^[A-Za-z0-9-]{1,64}$")]
    private static partial Regex AgentSession();
}
