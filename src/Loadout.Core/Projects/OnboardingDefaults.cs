using Loadout.Models.Configuration;
using Loadout.Models.Projects;

namespace Loadout.Core.Projects;

/// <summary>Something a default filled in, said plainly enough to print.</summary>
/// <param name="Setting">What was set, as a person would name it.</param>
/// <param name="Value">What it was set to.</param>
public sealed record OnboardingChoice(string Setting, string Value);

/// <summary>
/// Fills in a new project with what you already work like.
/// </summary>
/// <remarks>
/// <para>
/// These are the questions registering a project currently makes you answer
/// later — after the third time something surprises you. Which agent, which
/// model for which mode, which editor profile, which security profile. None of
/// them is new; what is new is being asked once rather than discovered
/// repeatedly.
/// </para>
/// <para>
/// A default never overwrites something already there. A project that names its
/// own agent has said so deliberately, and a machine-wide preference is not an
/// instruction to reconsider it. This only fills blanks, and reports what it
/// filled so nothing arrives silently.
/// </para>
/// </remarks>
public static class OnboardingDefaults
{
    /// <summary>
    /// Applies the defaults, and says what it applied.
    /// </summary>
    /// <param name="entry">The registration, which holds the agent and editor profile.</param>
    /// <param name="manifest">The project, which holds the models. Null when there is none.</param>
    /// <param name="settings">What was configured on this machine.</param>
    public static IReadOnlyList<OnboardingChoice> Apply(
        ProjectRegistryEntry entry,
        ProjectManifest? manifest,
        OnboardingSettings? settings)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var applied = new List<OnboardingChoice>();

        if (settings is null)
        {
            return applied;
        }

        // "claude" is the built-in default rather than a choice anybody made,
        // so a configured agent is allowed to replace it. Anything else was
        // chosen and is left alone.
        if (settings.Agent is { Length: > 0 } agent
            && (entry.DefaultAgent.Length == 0
                || string.Equals(entry.DefaultAgent, "claude", StringComparison.OrdinalIgnoreCase)))
        {
            if (!string.Equals(entry.DefaultAgent, agent, StringComparison.OrdinalIgnoreCase))
            {
                entry.DefaultAgent = agent;
                applied.Add(new OnboardingChoice("agent", agent));
            }
        }

        if (settings.EditorProfile is { Length: > 0 } profile && entry.EditorProfile.Length == 0)
        {
            entry.EditorProfile = profile;
            applied.Add(new OnboardingChoice("editor profile", profile));
        }

        if (manifest is null)
        {
            return applied;
        }

        if (settings.Model is { Length: > 0 } model && manifest.Agents.Model.Length == 0)
        {
            manifest.Agents.Model = model;
            applied.Add(new OnboardingChoice("model", model));
        }

        foreach (var (mode, byMode) in settings.ModelByMode)
        {
            if (byMode is { Length: > 0 } && !manifest.Agents.ModelByMode.ContainsKey(mode))
            {
                manifest.Agents.ModelByMode[mode] = byMode;
                applied.Add(new OnboardingChoice($"model for {mode}", byMode));
            }
        }

        return applied;
    }
}
