using System.Text.Json;
using System.Text.Json.Nodes;
using Loadout.Core.Instructions;

namespace Loadout.Core.Policies;

/// <summary>What screening a settings file produced.</summary>
/// <param name="Document">The settings as they will be handed to the agent.</param>
/// <param name="Dropped">Hook commands that were not applied, for saying so.</param>
/// <param name="Rewritten">How many of the launcher's own hooks were pointed at this machine's launcher.</param>
/// <param name="Changed">Whether the document differs from what was read.</param>
public sealed record ScreenedSettings(
    JsonObject Document,
    IReadOnlyList<string> Dropped,
    int Rewritten,
    bool Changed);

/// <summary>
/// Applies the rule for shared settings to the hooks in a Claude settings file.
/// </summary>
/// <remarks>
/// <para>
/// The rule is the one pre-approvals already follow: a file that travels
/// between people and machines may only tighten, and anything that loosens
/// has to come from configuration that stays on this machine. A hook is a
/// loosening — it is a command run after every edit — and the project's
/// settings file lives in the workspace, which syncs. Until now the file went
/// to the agent as it was. It went that way from the first commit, as the
/// mechanism for moving a repository's own <c>.claude/settings.json</c> out
/// of the repository, and was written before the rule was; it was never
/// weighed against the rule, and the after-edit hook is the first thing this
/// launcher itself writes there.
/// </para>
/// <para>
/// Three outcomes for a hook command. The launcher's own is kept and
/// rewritten to this machine's launcher, so the synced file need not name a
/// path at all. One listed in <c>commands.allowed_hooks</c> for the project,
/// in config.yaml, is kept as written. Anything else is dropped and named,
/// with the config that would allow it here. Everything in the file other
/// than hooks passes untouched.
/// </para>
/// </remarks>
public static class HookScreen
{
    private static readonly JsonDocumentOptions Lenient = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Screens a settings document read from text, or null when it is not a JSON object.</summary>
    public static ScreenedSettings? Screen(string text, IReadOnlyList<string> allowed, string launcher)
    {
        try
        {
            return JsonNode.Parse(text, nodeOptions: null, Lenient) is JsonObject root
                ? Screen(root, allowed, launcher)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Screens a settings document in place.</summary>
    /// <param name="root">The settings. Edited in place.</param>
    /// <param name="allowed">Commands, or prefixes of commands, allowed on this machine for the project.</param>
    /// <param name="launcher">How to start this launcher from a shell, quoted.</param>
    public static ScreenedSettings Screen(JsonObject root, IReadOnlyList<string> allowed, string launcher)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(allowed);
        ArgumentException.ThrowIfNullOrWhiteSpace(launcher);

        var dropped = new List<string>();
        var rewritten = 0;
        var changed = false;

        if (root["hooks"] is not JsonObject hooks)
        {
            return new ScreenedSettings(root, dropped, 0, false);
        }

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
                    var command = CommandOf(hook);

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

                    if (command is not null && IsAllowed(command, allowed))
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

        return new ScreenedSettings(root, dropped, rewritten, changed);
    }

    /// <summary>
    /// Whether a command is one this machine has said may run.
    /// </summary>
    /// <remarks>
    /// An entry matches the whole command or is a prefix of it ending at a
    /// word, so allowing <c>prettier</c> allows <c>prettier --write</c> and
    /// not <c>prettierish</c>. Exact and prefix rather than a pattern: an
    /// allow list is read by the person it protects, and a pattern is the
    /// form they would get wrong.
    /// </remarks>
    private static bool IsAllowed(string command, IReadOnlyList<string> allowed)
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

    private static string? CommandOf(JsonObject hook) =>
        hook["command"] is JsonValue value && value.TryGetValue<string>(out var command)
            ? command
            : null;
}
