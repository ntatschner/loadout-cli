using System.Text.Json;
using System.Text.RegularExpressions;

namespace Loadout.Core.Sessions.Hosting;

/// <summary>What an agent's own hook last said about a hosted session.</summary>
/// <param name="State">
/// <c>waiting</c> (it has finished and wants your next message), <c>approval</c>
/// (it is asking to be allowed something) or <c>turn</c> (a reply ended, which
/// says nothing about waiting and is kept only for the agent's session id).
/// </param>
/// <param name="AgentSession">The agent's own id for the conversation, which 'loadout resume' takes.</param>
/// <param name="At">When the hook ran.</param>
public sealed record SessionSignal(string State, string? AgentSession, DateTimeOffset At);

/// <summary>
/// Where an agent running in a hosted session reports that it is waiting,
/// and where the daemon reads it.
/// </summary>
/// <remarks>
/// <para>
/// A terminal only says whether something was written lately, which is
/// working, idle or unknown. Whether an agent is waiting for an answer is
/// something only the agent knows, so it comes from the agent's own hooks,
/// which the launch installs: never from reading the screen, which is a
/// guess that would sometimes put a question in front of somebody that was
/// never asked. The hook says that a session is waiting, not what the
/// choices are, which is why Studio sends people to the terminal to answer.
/// </para>
/// <para>
/// A file per session rather than a request to the daemon: the hook runs
/// inside the agent, which holds no sign-in and should not, and a file in the
/// owner-only state directory is reachable by nothing a browser can touch.
/// The daemon tells the session which file is its own through
/// <see cref="Variable"/>, which the launch and the agent both inherit.
/// </para>
/// <para>
/// The file outlives the daemon on purpose. A session it hosted ends when it
/// stops, and the file left behind is how the page can still offer
/// 'loadout resume' with the conversation's own id.
/// </para>
/// </remarks>
public static partial class SessionSignals
{
    /// <summary>Set on every hosted session's process to the daemon's id for it.</summary>
    public const string Variable = "LOADOUT_STUDIO_SESSION";

    /// <summary>The states a hook may report.</summary>
    public static readonly IReadOnlyList<string> States = ["waiting", "approval", "turn"];

    /// <summary>What a hook's input is read up to; a payload is a few hundred bytes.</summary>
    public const int MaximumInput = 64 * 1024;

    /// <summary>How long a file left by a session the daemon no longer hosts is kept.</summary>
    public static readonly TimeSpan Kept = TimeSpan.FromDays(7);

    /// <summary>The directory the signals live in, under the state directory.</summary>
    public static string Directory(string state) => Path.Combine(state, "studio", "signals");

    /// <summary>Whether this is an id the daemon could have given, so it is safe as a file name.</summary>
    public static bool IsSessionId(string? id) => id is not null && SessionId().IsMatch(id);

    /// <summary>Records what a hook said, keeping the agent's id from before when this one has none.</summary>
    /// <returns>False where the id or the state is not one this writes.</returns>
    public static bool Write(string directory, string id, string state, string? agentSession, DateTimeOffset at)
    {
        if (!IsSessionId(id) || !States.Contains(state, StringComparer.Ordinal))
        {
            return false;
        }

        // The agent's id goes on a command line the page shows, so anything
        // that is not plainly an id is dropped rather than shown.
        if (agentSession is not null && !AgentSessionId().IsMatch(agentSession))
        {
            agentSession = null;
        }

        agentSession ??= Read(directory, id)?.AgentSession;

        System.IO.Directory.CreateDirectory(directory);

        var path = PathOf(directory, id);
        var temporary = path + ".tmp";

        // Written aside and moved, so the daemon never reads half a file.
        File.WriteAllText(temporary, JsonSerializer.Serialize(new SessionSignal(state, agentSession, at), Json));
        File.Move(temporary, path, overwrite: true);

        return true;
    }

    /// <summary>What was last recorded for a session, or null for nothing or nothing readable.</summary>
    public static SessionSignal? Read(string directory, string id)
    {
        if (!IsSessionId(id))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SessionSignal>(File.ReadAllText(PathOf(directory, id)), Json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Every session with a signal, by its id, newest first.</summary>
    /// <remarks>
    /// Kept a week, which makes them the record of what an earlier daemon
    /// hosted: nothing else about a hosted session outlives the daemon.
    /// </remarks>
    public static IReadOnlyList<(string Id, SessionSignal Signal)> All(string directory)
    {
        if (!System.IO.Directory.Exists(directory))
        {
            return [];
        }

        var found = new List<(string Id, SessionSignal Signal)>();

        foreach (var file in System.IO.Directory.EnumerateFiles(directory, "*.json"))
        {
            var id = Path.GetFileNameWithoutExtension(file);

            if (Read(directory, id) is { } signal)
            {
                found.Add((id, signal));
            }
        }

        return found.OrderByDescending(entry => entry.Signal.At).ToList();
    }

    /// <summary>Forgets a session's signal, when the session itself is forgotten.</summary>
    public static void Remove(string directory, string id)
    {
        if (!IsSessionId(id))
        {
            return;
        }

        try
        {
            File.Delete(PathOf(directory, id));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Pruned later; a stale file only offers a resume command.
        }
    }

    /// <summary>Removes signals older than <see cref="Kept"/>.</summary>
    public static void Prune(string directory, DateTimeOffset now)
    {
        if (!System.IO.Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in System.IO.Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                if (now - File.GetLastWriteTimeUtc(file) > Kept)
                {
                    File.Delete(file);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Tried again on the next start.
            }
        }
    }

    /// <summary>
    /// What the page should say a running session needs: <c>waiting</c>,
    /// <c>approval</c>, or null for nothing the agent has said.
    /// </summary>
    /// <remarks>
    /// A signal counts only until somebody types into the session after it.
    /// A hosted session takes input only through the daemon, so that is a
    /// moment the daemon knows exactly, and an answer typed into the terminal
    /// clears the question without the agent having to say so.
    /// </remarks>
    public static string? Needs(SessionSignal? signal, DateTimeOffset? lastInput, bool ended) =>
        ended || signal is null || signal.State is not ("waiting" or "approval")
            ? null
            : lastInput is { } typed && typed >= signal.At ? null : signal.State;

    private static string PathOf(string directory, string id) => Path.Combine(directory, id + ".json");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [GeneratedRegex("^[0-9a-f]{8}$")]
    private static partial Regex SessionId();

    [GeneratedRegex("^[A-Za-z0-9-]{1,64}$")]
    private static partial Regex AgentSessionId();
}
