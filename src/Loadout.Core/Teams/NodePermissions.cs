using System.Text.Json;

namespace Loadout.Core.Teams;

/// <summary>What one node of a run may do, written where the answerer can read it.</summary>
/// <param name="Run">The run this belongs to.</param>
/// <param name="Node">The node, as the run named it.</param>
/// <param name="Role">The role it is playing, for the reason given back to it.</param>
/// <param name="Allow">Rules that permit, in the agent's own spelling.</param>
/// <param name="Deny">Rules that forbid, in the agent's own spelling. Deny wins.</param>
public sealed record NodePolicy(
    string Run,
    string Node,
    string Role,
    IReadOnlyList<string> Allow,
    IReadOnlyList<string> Deny);

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

    /// <summary>A node name as a file name: instance names carry a slash.</summary>
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
