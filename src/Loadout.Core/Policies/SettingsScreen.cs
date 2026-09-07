using System.Text.Json;
using System.Text.Json.Nodes;
using Loadout.Core.Instructions;

namespace Loadout.Core.Policies;

/// <summary>What screening a settings file produced.</summary>
/// <param name="Document">The settings as they will be handed to the agent.</param>
/// <param name="DroppedHooks">Hook commands that were not applied, for saying so.</param>
/// <param name="DroppedApprovals">Entries of <c>permissions.allow</c> that were not applied.</param>
/// <param name="DroppedSettings">Other loosening settings that were not applied, as <c>key: value</c>.</param>
/// <param name="Rewritten">How many of the launcher's own hooks were pointed at this machine's launcher.</param>
/// <param name="Changed">Whether the document differs from what was read.</param>
public sealed record ScreenedSettings(
    JsonObject Document,
    IReadOnlyList<string> DroppedHooks,
    IReadOnlyList<string> DroppedApprovals,
    IReadOnlyList<string> DroppedSettings,
    int Rewritten,
    bool Changed);

/// <summary>
/// Applies the rule for shared settings to a Claude settings file.
/// </summary>
/// <remarks>
/// <para>
/// The rule is the one pre-approvals already follow: a file that travels
/// between people and machines may only tighten, and anything that loosens
/// has to come from configuration that stays on this machine. The project's
/// settings file lives in the workspace, which syncs. It went to the agent
/// as it was from the first commit, as the mechanism for moving a
/// repository's own <c>.claude/settings.json</c> out of the repository, and
/// was written before the rule was; it was never weighed against the rule.
/// </para>
/// <para>
/// Three things in the file loosen. A hook is a command run after every
/// edit: the launcher's own is kept and pointed at this machine's launcher,
/// one listed in <c>commands.allowed_hooks</c> for the project is kept as
/// written, and the rest are dropped. An entry in <c>permissions.allow</c>
/// removes an approval prompt: it is kept only where this machine's
/// <c>commands.pre_approved</c> already says the same, which is where the
/// security profile's own allow list is sent too. A permission mode that
/// skips prompts, or a directory added to the agent's reach, is dropped
/// outright, because nothing local expresses either. Everything else —
/// denials, the ask list, a theme, the status line — passes untouched.
/// </para>
/// </remarks>
public static class SettingsScreen
{
    private static readonly HashSet<string> ModesThatOnlyTighten = new(StringComparer.Ordinal)
    {
        "default", "plan",
    };

    private static readonly JsonDocumentOptions Lenient = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Screens a settings document read from text, or null when it is not a JSON object.</summary>
    public static ScreenedSettings? Screen(
        string text,
        IReadOnlyList<string> allowedHooks,
        IReadOnlyList<string> preApproved,
        string launcher)
    {
        try
        {
            return JsonNode.Parse(text, nodeOptions: null, Lenient) is JsonObject root
                ? Screen(root, allowedHooks, preApproved, launcher)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Screens a settings document in place.</summary>
    /// <param name="root">The settings. Edited in place.</param>
    /// <param name="allowedHooks">Commands, or word prefixes of commands, allowed to run as hooks here.</param>
    /// <param name="preApproved">
    /// Tool specifiers this machine has pre-approved for the project, in the
    /// forms the adapter sends Claude, so an entry the file and the machine
    /// agree on survives.
    /// </param>
    /// <param name="launcher">How to start this launcher from a shell, quoted.</param>
    public static ScreenedSettings Screen(
        JsonObject root,
        IReadOnlyList<string> allowedHooks,
        IReadOnlyList<string> preApproved,
        string launcher)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(allowedHooks);
        ArgumentNullException.ThrowIfNull(preApproved);
        ArgumentException.ThrowIfNullOrWhiteSpace(launcher);

        var droppedHooks = new List<string>();
        var droppedApprovals = new List<string>();
        var droppedSettings = new List<string>();
        var rewritten = 0;
        var changed = false;

        changed |= ScreenHooks(root, allowedHooks, launcher, droppedHooks, ref rewritten);
        changed |= ScreenPermissions(root, preApproved, droppedApprovals, droppedSettings);

        return new ScreenedSettings(root, droppedHooks, droppedApprovals, droppedSettings, rewritten, changed);
    }

    private static bool ScreenHooks(
        JsonObject root,
        IReadOnlyList<string> allowed,
        string launcher,
        List<string> dropped,
        ref int rewritten)
    {
        if (root["hooks"] is not JsonObject hooks)
        {
            return false;
        }

        var changed = false;

        foreach (var eventName in hooks.Select(pair => pair.Key).ToList())
        {
            if (hooks[eventName] is not JsonArray entries)
            {
                continue;
            }

            foreach (var entry in entries.OfType<JsonObject>().ToList())
            {
                if (entry["hooks"] is not JsonArray list)
                {
                    continue;
                }

                foreach (var hook in list.OfType<JsonObject>().ToList())
                {
                    var command = Text(hook["command"]);

                    if (command is not null && RefreshHook.IsOwn(command))
                    {
                        var local = RefreshHook.Localise(command, launcher);

                        if (local != command)
                        {
                            hook["command"] = local;
                            rewritten++;
                            changed = true;
                        }

                        continue;
                    }

                    if (command is not null && IsAllowedHook(command, allowed))
                    {
                        continue;
                    }

                    list.Remove(hook);
                    dropped.Add(command ?? "(a hook with no command)");
                    changed = true;
                }

                if (list.Count == 0)
                {
                    entries.Remove(entry);
                }
            }

            if (entries.Count == 0)
            {
                hooks.Remove(eventName);
            }
        }

        if (hooks.Count == 0)
        {
            root.Remove("hooks");
        }

        return changed;
    }

    /// <summary>
    /// The loosening half of <c>permissions</c>: the allow list, the mode and
    /// the extra directories. Denials and the ask list only tighten and pass.
    /// </summary>
    private static bool ScreenPermissions(
        JsonObject root,
        IReadOnlyList<string> preApproved,
        List<string> droppedApprovals,
        List<string> droppedSettings)
    {
        if (root["permissions"] is not JsonObject permissions)
        {
            return false;
        }

        var changed = false;

        if (permissions["allow"] is JsonArray allow)
        {
            foreach (var item in allow.ToList())
            {
                var entry = Text(item)?.Trim();

                if (entry is not null && preApproved.Any(p => p.Trim().Equals(entry, StringComparison.Ordinal)))
                {
                    continue;
                }

                allow.Remove(item);
                droppedApprovals.Add(entry ?? "(an entry that is not a string)");
                changed = true;
            }

            if (allow.Count == 0)
            {
                permissions.Remove("allow");
            }
        }

        if (permissions["defaultMode"] is { } modeNode)
        {
            var mode = Text(modeNode);

            if (mode is null || !ModesThatOnlyTighten.Contains(mode))
            {
                permissions.Remove("defaultMode");
                droppedSettings.Add($"permissions.defaultMode: {mode ?? "(not a string)"}");
                changed = true;
            }
        }

        if (permissions["additionalDirectories"] is { } directories)
        {
            permissions.Remove("additionalDirectories");
            droppedSettings.Add($"permissions.additionalDirectories: {directories.ToJsonString()}");
            changed = true;
        }

        if (permissions.Count == 0)
        {
            root.Remove("permissions");
        }

        return changed;
    }

    /// <summary>
    /// Whether a hook command is one this machine has said may run.
    /// </summary>
    /// <remarks>
    /// An entry matches the whole command or is a prefix of it ending at a
    /// word, so allowing <c>prettier</c> allows <c>prettier --write</c> and
    /// not <c>prettierish</c>. Exact and prefix rather than a pattern: an
    /// allow list is read by the person it protects, and a pattern is the
    /// form they would get wrong.
    /// </remarks>
    private static bool IsAllowedHook(string command, IReadOnlyList<string> allowed)
    {
        var trimmed = command.Trim();

        foreach (var entry in allowed)
        {
            var wanted = entry.Trim();

            if (wanted.Length == 0)
            {
                continue;
            }

            if (trimmed.Equals(wanted, StringComparison.Ordinal))
            {
                return true;
            }

            if (trimmed.StartsWith(wanted, StringComparison.Ordinal)
                && trimmed.Length > wanted.Length
                && char.IsWhiteSpace(trimmed[wanted.Length]))
            {
                return true;
            }
        }

        return false;
    }

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
