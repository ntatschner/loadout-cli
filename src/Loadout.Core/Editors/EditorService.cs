using System.Text.Json;
using System.Text.Json.Nodes;
using Loadout.Models;
using Loadout.Models.Configuration;
using Loadout.Models.Projects;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Editors;

/// <summary>
/// What is known about the editor on this machine.
/// </summary>
/// <param name="Command">The command that was looked for, such as <c>code</c>.</param>
/// <param name="Path">Where it was found, or null when it is not installed.</param>
/// <param name="Profiles">
/// The profiles the editor has, or null when they could not be determined.
/// Null and empty mean different things: empty is "this editor has no profiles
/// beyond the default", null is "this could not be read", and only the first of
/// those justifies telling somebody a profile is missing.
/// </param>
public sealed record EditorState(
    string Command,
    string? Path,
    IReadOnlyList<string>? Profiles)
{
    /// <summary>Whether the editor is on this machine at all.</summary>
    public bool IsInstalled => Path is not null;

    /// <summary>
    /// Whether a named profile is known to be missing. False when the profiles
    /// could not be read, because "I could not check" must never be reported as
    /// "it is not there".
    /// </summary>
    public bool IsMissing(string profile) =>
        Profiles is not null
        && profile.Length > 0
        && !Profiles.Contains(profile, StringComparer.Ordinal);
}

/// <summary>
/// Opens a project in an editor, under the profile that suits the agent.
/// </summary>
public interface IEditorService
{
    /// <summary>Finds the editor and, where it can, the profiles it has.</summary>
    EditorState Describe(LauncherConfig config);

    /// <summary>
    /// The profile a project should open under: its own if it names one, then
    /// the one configured for the agent it uses, then none.
    /// </summary>
    string? ProfileFor(LauncherConfig config, ProjectRegistryEntry project, string? agent = null);

    /// <summary>Opens a directory in the editor.</summary>
    Task<OperationResult> OpenAsync(
        LauncherConfig config,
        ProjectRegistryEntry project,
        string directory,
        string? agent = null,
        CancellationToken ct = default);
}

/// <inheritdoc />
internal sealed class EditorService : IEditorService
{
    /// <summary>
    /// Where VS Code records the profiles somebody has made.
    /// <para>
    /// Read, never written. This is an internal file rather than a published
    /// interface, so every failure to make sense of it is reported as "could
    /// not be determined" rather than as an absence — a wrong "that profile
    /// does not exist" is worse than no answer, because it sends somebody
    /// looking for a problem they do not have.
    /// </para>
    /// </summary>
    private const string ProfileStore = "User/globalStorage/storage.json";

    private readonly IExecutableResolver _executables;
    private readonly IProcessLauncher _processes;

    public EditorService(IExecutableResolver executables, IProcessLauncher processes)
    {
        _executables = executables;
        _processes = processes;
    }

    /// <inheritdoc />
    public EditorState Describe(LauncherConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var command = string.IsNullOrWhiteSpace(config.Editor.Command)
            ? "code"
            : config.Editor.Command;

        var path = _executables.Resolve(command);

        return new EditorState(command, path, ReadProfiles(command));
    }

    /// <inheritdoc />
    public string? ProfileFor(
        LauncherConfig config,
        ProjectRegistryEntry project,
        string? agent = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(project);

        // A project that names one has said so deliberately, and overrides
        // whatever its agent would otherwise have chosen.
        if (!string.IsNullOrWhiteSpace(project.EditorProfile))
        {
            return project.EditorProfile;
        }

        var name = agent ?? project.DefaultAgent;

        return config.Editor.Profiles.TryGetValue(name, out var profile)
            && !string.IsNullOrWhiteSpace(profile)
                ? profile
                : null;
    }

    /// <inheritdoc />
    public async Task<OperationResult> OpenAsync(
        LauncherConfig config,
        ProjectRegistryEntry project,
        string directory,
        string? agent = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var editor = Describe(config);

        if (editor.Path is null)
        {
            return OperationResult.Fail(
                $"'{editor.Command}' is not installed, or is not on PATH. "
                + "Set editor.command in config.yaml if it is called something else here.",
                ExitCode.GeneralFailure);
        }

        // The folder, and deliberately nothing else.
        //
        // Asking for a profile stops the folder opening at all: the editor
        // comes up as a window with no folder and no workbench in it, and says
        // nothing about why. It is not the handoff to a running instance, a
        // second instance, the inherited environment, the working directory or
        // the new-window flag — each of those was suspected, and each was
        // cleared by testing it. It is --profile itself, and bisecting the
        // command line separates it cleanly:
        //
        //     code --profile <name> <folder>      window, no folder
        //     code -n --profile <name> <folder>   window, no folder
        //     code <folder> --profile <name>      window, no folder
        //     code <folder>                       opens
        //     code -n <folder>                    opens
        //
        // Asked for on its own, 'code --profile <name>' opens that profile
        // perfectly well, and the profile answers command line queries put to
        // it, so the profile is not broken. The combination is.
        //
        // Opening in the default profile is a smaller problem than not opening
        // at all, so the profile is left off — and the caller says so, because
        // a downgrade nobody mentions is how this went unexplained for so long.
        List<string> arguments = [directory];

        var result = _processes.StartDetached(
            new ProcessRequest(
                editor.Path,
                arguments,

                // Started inside the folder it is opening, and that is not
                // incidental. Launched from the Start Menu the launcher's own
                // working directory is the directory it is installed in, the
                // editor inherited that, and it came up as an empty frame.
                // Every invocation that has ever worked had a working directory
                // that was a repository; every blank one had somewhere that was
                // not.
                //
                // This was removed once, on the reasoning that the folder is
                // already an argument so the directory could not matter. The
                // probe showed otherwise.
                directory,

                // Opened from a terminal the editor owns, the editor hands us a
                // copy of its own private environment, and passing that back to
                // a new instance breaks it. VS Code sets ELECTRON_RUN_AS_NODE=1
                // for its command line shim, so the copy we inherit makes the
                // editor we start run as Node: it read the folder as a module
                // path and answered "Cannot find module".
                //
                // The whole family goes, including VSCODE_IPC_HOOK_CLI. Keeping
                // that one back was tried, on the theory that handing the folder
                // to an instance already running was what made the difference.
                // It was not: with every one of these withheld, the folder opens
                // whether or not another editor is running.
                RemoveEnvironmentPrefixes: ["VSCODE_", "ELECTRON_", "CHROME_"]));

        return result.Succeeded
            ? OperationResult.Ok()
            : OperationResult.Fail(result.Error ?? "The editor could not be started.", result.ExitCode);
    }

    /// <summary>
    /// Best-effort read of the editor's profiles. Null on anything unexpected.
    /// </summary>
    private IReadOnlyList<string>? ReadProfiles(string command)
    {
        var directory = UserDataDirectory(command);

        if (directory is null)
        {
            return null;
        }

        var storage = Path.Combine(directory, ProfileStore);

        if (!File.Exists(storage))
        {
            return null;
        }

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(storage));

            // The default profile is not in this list and needs no naming: it
            // is what the editor opens with when nothing is asked for.
            if (root?["userDataProfiles"] is not JsonArray profiles)
            {
                return [];
            }

            return [.. profiles
                .Select(entry => entry?["name"]?.GetValue<string>())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)];
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException
                or InvalidOperationException
                or FormatException)
        {
            // Unreadable, or not the shape this expects any more. Both mean the
            // same thing to a caller: no answer, rather than a wrong one.
            return null;
        }
    }

    /// <summary>
    /// Where the editor keeps per-user state, which differs per platform and
    /// per fork. Null where there is no sensible guess, which is not an error.
    /// </summary>
    private string? UserDataDirectory(string command)
    {
        // The directory is named after the product, not the command, and the
        // two differ for every fork. Only the ones that can be mapped with
        // confidence are mapped; anything else reports "unknown" rather than
        // guessing at somebody's disk.
        var product = command switch
        {
            "code" => "Code",
            "code-insiders" => "Code - Insiders",
            "codium" or "vscodium" => "VSCodium",
            "cursor" => "Cursor",
            _ => null,
        };

        if (product is null)
        {
            return null;
        }

        if (OperatingSystem.IsWindows())
        {
            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            return roaming.Length == 0 ? null : Path.Combine(roaming, product);
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (string.IsNullOrEmpty(home))
        {
            return null;
        }

        return OperatingSystem.IsMacOS()
            ? Path.Combine(home, "Library", "Application Support", product)
            : Path.Combine(home, ".config", product);
    }
}
