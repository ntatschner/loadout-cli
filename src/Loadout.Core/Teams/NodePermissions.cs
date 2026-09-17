using System.Text.Json;

namespace Loadout.Core.Teams;

/// <summary>What one node of a run may do, written where the answerer can read it.</summary>
/// <param name="Run">The run this belongs to.</param>
/// <param name="Node">The node, as the run named it.</param>
/// <param name="Role">The role it is playing, for the reason given back to it.</param>
/// <param name="Allow">Rules that permit, in the agent's own spelling.</param>
/// <param name="Deny">Rules that forbid, in the agent's own spelling. Deny wins.</param>
/// <param name="Ask">
/// Whether a call no rule covers may be put to the person rather than refused.
/// </param>
/// <remarks>
/// <para>
/// <paramref name="Ask"/> is false unless the run knows somebody is watching
/// it. An autonomous run has nobody; a run down a pipe has nobody; and a
/// question nobody answers is a node sitting still and spending until it gives
/// up, which is worse than the refusal it would otherwise have had.
/// </para>
/// <para>
/// It never reaches a rule in <see cref="Deny"/>. A role that forbids something
/// has already decided, and asking anyway would make every deny list a
/// suggestion.
/// </para>
/// </remarks>
public sealed record NodePolicy(
    string Run,
    string Node,
    string Role,
    IReadOnlyList<string> Allow,
    IReadOnlyList<string> Deny,
    bool Ask = false);

/// <summary>
/// Something a run has stopped on, waiting for a person.
/// </summary>
/// <param name="Id">Names the pair of files this and its answer live in.</param>
/// <param name="Node">The node that stopped, or the run's lead for a run-level gate.</param>
/// <param name="Role">What it is playing, so the question can say who is asking.</param>
/// <param name="Tool">The tool it asked about, for a permission.</param>
/// <param name="Target">The one thing the call is pointed at, where there is one.</param>
/// <param name="At">When it asked.</param>
/// <param name="Kind">
/// <c>permission</c> for a call no rule covers, <c>confirm</c> for a step a
/// manual run holds, <c>question</c> for a decision a lead may not make.
/// </param>
/// <param name="Asked">
/// The question in the run's own words, for the kinds that have one. A
/// permission builds its own from the node, the tool and the target.
/// </param>
/// <param name="Options">
/// What may be chosen. Empty means yes or no, which is what a permission and a
/// confirm are.
/// </param>
/// <param name="Recommendation">What the lead would choose, for a question.</param>
/// <remarks>
/// One shape for all three because they are one thing to whoever is answering:
/// the run has stopped and wants a person. They were separate while only a
/// terminal could answer - a terminal can block on each in its own way - and a
/// browser cannot block on anything, so it needs them written down.
/// </remarks>
public sealed record PendingAsk(
    string Id,
    string Node,
    string Role,
    string Tool,
    string? Target,
    DateTimeOffset At,
    string Kind = "permission",
    string? Asked = null,
    IReadOnlyList<string>? Options = null,
    string? Recommendation = null)
{
    /// <summary>The question, as a person reads it.</summary>
    /// <remarks>
    /// Names the node, the role and what it is pointed at, because "may it run
    /// Bash?" is not a question anybody can answer. The target is already
    /// redacted by the time it is written.
    /// <para>
    /// No question mark: whoever asks adds one, which is the convention every
    /// other gate here follows. Having one too meant the first real run asked
    /// somebody "Let it??".
    /// </para>
    /// </remarks>
    public string Question =>
        Asked is { Length: > 0 } said
            ? said
            : $"{Node} ({Role}) wants to use {Tool}"
                + (Target is { Length: > 0 } ? $" for '{Target}'" : string.Empty)
                + ". Nothing in its role allows that. Let it";

    /// <summary>What may be chosen, with yes and no as the default pair.</summary>
    public IReadOnlyList<string> Choices =>
        Options is { Count: > 0 } given ? given : ["yes", "no"];
}

/// <summary>What a person said about one call.</summary>
/// <param name="Allowed">Whether it may. For a question, whether one was chosen at all.</param>
/// <param name="Reason">What to tell the node, whichever way it went.</param>
/// <param name="Chosen">The option picked, for a question that had several.</param>
/// <param name="By">
/// Where the answer came from: <c>terminal</c> or <c>dashboard</c>. Kept
/// because "somebody allowed this" and "somebody allowed this from a browser
/// on the other side of the house" are different sentences to whoever reads
/// the run back.
/// </param>
public sealed record AskAnswer(
    bool Allowed,
    string Reason,
    string? Chosen = null,
    string By = "terminal");

/// <summary>An answer to "may I do this", and why.</summary>
/// <param name="Allowed">Whether it may.</param>
/// <param name="Reason">Why, in words the node can act on. Never empty.</param>
/// <param name="Rule">The rule that settled it, or null when nothing matched.</param>
public sealed record PermissionDecision(bool Allowed, string Reason, string? Rule = null);

/// <summary>
/// Decides what a node may do when its agent stops to ask.
/// </summary>
/// <remarks>
/// <para>
/// Without this, anything a node's allow list did not already cover was
/// denied by the agent with nothing said: the node saw a refusal it could not
/// explain, the run recorded a count, and whoever read it afterwards could
/// not tell a role that was too narrow from a node that tried something it
/// should not have.
/// </para>
/// <para>
/// The rules are the role's own, in the spelling the role files already use,
/// which is the agent's: a bare tool name, or a tool with a pattern for the
/// one thing the call is pointed at. Reading the role and reading the agent's
/// settings are then the same skill.
/// </para>
/// <para>
/// Deny wins, and what nothing matches is denied. A node whose brief does not
/// say it may do a thing may not do it - the safe direction when nobody is
/// there to ask, and the reason says which of the two it was.
/// </para>
/// </remarks>
public static class NodePermissions
{
    /// <summary>The file a run writes for a node, under the run's own directory.</summary>
    public static string FileName(string node) => $"policy-{Safe(node)}.json";

    /// <summary>Where the answerer records what it was asked.</summary>
    public static string AskedFileName(string node) => $"asked-{Safe(node)}.jsonl";

    /// <summary>How long a node waits for a person before giving up.</summary>
    /// <remarks>
    /// A node waiting is a node spending nothing and doing nothing, so this is
    /// not about cost. It is about a run that somebody walked away from ending
    /// in a refusal it can report rather than sitting there until the agent's
    /// own timeout kills it with nothing written down.
    /// </remarks>
    public static readonly TimeSpan Patience = TimeSpan.FromMinutes(5);

    /// <summary>How often each side looks for the other's file.</summary>
    public static readonly TimeSpan Glance = TimeSpan.FromMilliseconds(250);

    private static string AskPath(string directory, string id) =>
        Path.Combine(directory, $"ask-{Safe(id)}.json");

    private static string AnswerPath(string directory, string id) =>
        Path.Combine(directory, $"answer-{Safe(id)}.json");

    /// <summary>
    /// Puts a call to whoever is running the team, and waits for the answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two files in the run's own directory rather than anything cleverer. The
    /// answerer is a different process from the coordinator - the agent starts
    /// it, not us - so they have no channel between them but the directory the
    /// run already writes to, and a directory is a channel that survives either
    /// side restarting.
    /// </para>
    /// <para>
    /// Null when nobody answered in time. The caller refuses and says that is
    /// why, which is a different thing from the role refusing and reads
    /// differently to whoever picks the run up afterwards.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Writes the question where the run will find it, and says whether it got
    /// there.
    /// </summary>
    /// <remarks>
    /// Separate from the waiting because they fail differently and are worth
    /// telling apart: a question that could not be written is one nobody will
    /// ever see, and a question that was written and not answered is one
    /// somebody walked away from.
    /// </remarks>
    public static async Task<bool> PutAsync(
        string directory,
        PendingAsk ask,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ask);

        try
        {
            Directory.CreateDirectory(directory);

            await File.WriteAllTextAsync(
                AskPath(directory, ask.Id), JsonSerializer.Serialize(ask), ct).ConfigureAwait(false);

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static async Task<AskAnswer?> AskAsync(
        string directory,
        PendingAsk ask,
        TimeProvider time,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ask);
        ArgumentNullException.ThrowIfNull(time);

        if (!await PutAsync(directory, ask, ct).ConfigureAwait(false))
        {
            return null;
        }

        var until = time.GetUtcNow() + Patience;
        var answer = AnswerPath(directory, ask.Id);

        while (time.GetUtcNow() < until)
        {
            ct.ThrowIfCancellationRequested();

            if (File.Exists(answer))
            {
                try
                {
                    return JsonSerializer.Deserialize<AskAnswer>(
                        await File.ReadAllTextAsync(answer, ct).ConfigureAwait(false));
                }
                catch (Exception ex) when (ex is JsonException or IOException)
                {
                    // Half written: the other side is still putting it there.
                }
            }

            await Task.Delay(Glance, time, ct).ConfigureAwait(false);
        }

        return null;
    }

    /// <summary>Questions in this run that nobody has answered yet.</summary>
    public static IReadOnlyList<PendingAsk> Pending(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var waiting = new List<PendingAsk>();

        foreach (var file in Directory.EnumerateFiles(directory, "ask-*.json"))
        {
            try
            {
                if (JsonSerializer.Deserialize<PendingAsk>(File.ReadAllText(file)) is not { } ask
                    || File.Exists(AnswerPath(directory, ask.Id)))
                {
                    continue;
                }

                waiting.Add(ask);
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // Being written as it was read. The next glance gets it.
            }
        }

        return [.. waiting.OrderBy(ask => ask.At)];
    }

    /// <summary>Answers one question, for the node waiting on it to read.</summary>
    public static async Task AnswerAsync(
        string directory,
        string id,
        AskAnswer answer,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(answer);

        // Written beside, then moved into place, so the waiting side never
        // reads half an answer and treats it as no answer.
        var settled = AnswerPath(directory, id);
        var writing = settled + ".writing";

        await File.WriteAllTextAsync(writing, JsonSerializer.Serialize(answer), ct).ConfigureAwait(false);

        File.Move(writing, settled, overwrite: true);
    }

    /// <summary>A node name as a file name: instance names carry a slash.</summary>
    public static string FileSafe(string node) => Safe(node);

    private static string Safe(string node) =>
        string.Concat(node.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-'));

    /// <summary>Writes a node's policy, returning where it went.</summary>
    public static async Task<string> WriteAsync(string directory, NodePolicy policy, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var path = Path.Combine(directory, FileName(policy.Node));

        Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(policy, new JsonSerializerOptions { WriteIndented = true }),
            ct).ConfigureAwait(false);

        return path;
    }

    /// <summary>Reads a policy, or null when it is missing or unreadable.</summary>
    public static NodePolicy? Read(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<NodePolicy>(File.ReadAllText(path))
                : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A policy that cannot be read is not a policy that permits
            // anything. The caller denies and says so.
            return null;
        }
    }

    /// <summary>Whether this node may make this call.</summary>
    public static PermissionDecision Decide(NodePolicy? policy, string tool, string? inputJson)
    {
        if (policy is null)
        {
            return new PermissionDecision(
                false,
                "This session has no policy to answer from, so nothing beyond what it was already "
                + "given is allowed. Report what you could not do rather than working around it.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(tool);

        var target = Target(inputJson);

        foreach (var rule in policy.Deny)
        {
            if (Matches(rule, tool, target))
            {
                return new PermissionDecision(
                    false,
                    $"The {policy.Role} role forbids this: '{rule}'. Report it as a blocker rather "
                    + "than finding another way to do it.",
                    rule);
            }
        }

        foreach (var rule in policy.Allow)
        {
            if (Matches(rule, tool, target))
            {
                return new PermissionDecision(true, $"The {policy.Role} role allows '{rule}'.", rule);
            }
        }

        return new PermissionDecision(
            false,
            $"Nothing in the {policy.Role} role allows {tool}"
            + (target is { Length: > 0 } ? $" for '{target}'" : string.Empty)
            + ". Report what you needed and why, and let whoever briefed you decide.");
    }

    /// <summary>
    /// Whether a call is worth putting to a person rather than answering from
    /// the policy alone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three things have to be true, and each is load-bearing. The run has to
    /// have said somebody is watching. The call has to have been refused,
    /// because asking about one already allowed would stop a node on something
    /// its role said yes to. And nothing may have matched it — a rule that
    /// decided has decided, and a deny somebody could be asked past would make
    /// every deny list a suggestion.
    /// </para>
    /// <para>
    /// Here rather than at the answerer, because this is the boundary and the
    /// boundary is one place. A condition spelled out at a call site is a
    /// condition the next call site spells differently.
    /// </para>
    /// </remarks>
    public static bool Askable(NodePolicy? policy, PermissionDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);

        return policy is { Ask: true } && !decision.Allowed && decision.Rule is null;
    }

    /// <summary>
    /// Whether one rule covers one call.
    /// </summary>
    /// <remarks>
    /// Two shapes, both the agent's own: <c>Read</c> for any use of a tool,
    /// and <c>Bash(git status:*)</c> for a tool pointed at something in
    /// particular. A trailing <c>:*</c> or <c>*</c> is a prefix; anything else
    /// inside the brackets must match exactly. Deliberately not a glob - a
    /// rule nobody can predict the meaning of is worse than a narrow one.
    /// </remarks>
    public static bool Matches(string rule, string tool, string? target)
    {
        if (string.IsNullOrWhiteSpace(rule))
        {
            return false;
        }

        var open = rule.IndexOf('(', StringComparison.Ordinal);

        if (open < 0 || !rule.EndsWith(')'))
        {
            return string.Equals(rule.Trim(), tool, StringComparison.OrdinalIgnoreCase);
        }

        if (!string.Equals(rule[..open].Trim(), tool, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var pattern = rule[(open + 1)..^1].Trim();

        if (pattern is "*" or ":*")
        {
            return true;
        }

        if (target is not { Length: > 0 })
        {
            return false;
        }

        if (pattern.EndsWith(":*", StringComparison.Ordinal))
        {
            return target.StartsWith(pattern[..^2], StringComparison.Ordinal);
        }

        if (pattern.EndsWith('*'))
        {
            return target.StartsWith(pattern[..^1], StringComparison.Ordinal);
        }

        return string.Equals(target, pattern, StringComparison.Ordinal);
    }

    /// <summary>The one thing a call is pointed at, from the names tools use for it.</summary>
    public static string? Target(string? inputJson)
    {
        if (string.IsNullOrWhiteSpace(inputJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(inputJson);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var name in (string[])["command", "file_path", "path", "url", "pattern"])
            {
                if (document.RootElement.TryGetProperty(name, out var value)
                    && value.ValueKind == JsonValueKind.String
                    && value.GetString() is { Length: > 0 } text)
                {
                    return text;
                }
            }
        }
        catch (JsonException)
        {
            // A call whose input is not an object is judged on its tool name
            // alone, which is what a rule without brackets says anyway.
        }

        return null;
    }
}
