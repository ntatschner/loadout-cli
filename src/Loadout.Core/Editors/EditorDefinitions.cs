using Loadout.Models.Configuration;
using Loadout.Models.Editors;

namespace Loadout.Core.Editors;

/// <summary>
/// The editors this launcher knows without being told.
/// </summary>
/// <remarks>
/// Knowledge rather than data, which is why it is here and not in the model. An
/// entry earns its place by knowing something a configuration file could not
/// state as plainly — how the editor takes a profile, and whether it needs a
/// terminal. An editor that differs from the fallback only in its command name
/// is already served by <c>editor-command</c> and is deliberately absent.
/// </remarks>
internal static class EditorDefinitions
{
    /// <summary>Environment a graphical editor must not inherit from its own terminal.</summary>
    private static readonly string[] ElectronPoison = ["VSCODE_", "ELECTRON_", "CHROME_"];

    /// <summary>
    /// The definition to open <paramref name="command"/> with: what the
    /// configuration says, then what is known here, then the least this can
    /// assume.
    /// </summary>
    /// <remarks>
    /// Configuration wins outright, including over a built-in of the same name.
    /// That is the escape hatch for an editor whose invocation changed after
    /// this was built, and it mirrors what a custom agent already does.
    /// </remarks>
    internal static EditorDefinition For(LauncherConfig config, string command)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (config.CustomEditors.TryGetValue(command, out var declared) && declared is not null)
        {
            return declared;
        }

        return BuiltIn(command) ?? Fallback();
    }

    /// <summary>What is known about an editor by name, or null for one that is not.</summary>
    internal static EditorDefinition? BuiltIn(string command) => command switch
    {
        "code" or "code-insiders" or "codium" or "vscodium" or "cursor" => new EditorDefinition
        {
            Arguments = ["${DIRECTORY}"],

            // Empty on purpose. See the bisect in EditorService.OpenAsync: a
            // folder and a profile asked for together open neither.
            ProfileArguments = [],
            ProfileNote = "will not open a folder and a profile together.",
            RemoveEnvironmentPrefixes = [.. ElectronPoison],
        },

        // The cleanest profile mechanism going, and the reason this seam is
        // worth having: one environment variable names the whole configuration
        // directory the editor loads, so two profiles are two directories and
        // nothing has to be written to switch between them.
        "nvim" => new EditorDefinition
        {
            Arguments = ["${DIRECTORY}"],
            ProfileEnvironment = "NVIM_APPNAME",
            Terminal = true,
        },

        _ => null,
    };

    /// <summary>
    /// The least that can be assumed about an editor nobody has described: it
    /// takes a folder, and nothing else is claimed about it.
    /// </summary>
    /// <remarks>
    /// No profile mechanism, which is not the same as a broken one. An editor
    /// reached this way is never reported as having ignored a profile, because
    /// nothing here knows it has profiles to ignore.
    /// </remarks>
    private static EditorDefinition Fallback() => new()
    {
        Arguments = ["${DIRECTORY}"],
    };
}
