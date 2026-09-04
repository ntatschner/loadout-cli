using System.Text.Json;
using System.Text.Json.Nodes;
using Loadout.Models;
using Loadout.Models.Configuration;
using Loadout.Models.Editors;
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
/// <param name="Definition">
/// How this editor is started and told a profile. Null only where nothing
/// resolved one, which callers read as "no profile mechanism" rather than as
/// "the mechanism is broken".
/// </param>
public sealed record EditorState(
    string Command,
    string? Path,
    IReadOnlyList<string>? Profiles,
    EditorDefinition? Definition = null)
{
    /// <summary>
    /// Whether this editor can be told a profile at all.
    /// </summary>
    /// <remarks>
    /// Separates "the profile was ignored" from "there is nothing here to
    /// ignore it with". Telling somebody their profile was dropped by an editor
    /// that has no profiles is a note about a setting they never made.
    /// </remarks>
    public bool CanOpenAProfile =>
        Definition is { } definition
        && (definition.ProfileArguments.Count > 0
            || !string.IsNullOrWhiteSpace(definition.ProfileEnvironment));

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

        var definition = EditorDefinitions.For(config, command);

        // The key names the editor; the definition may name a different binary
        // for it, which is what lets one be declared for a fork that ships
        // under another name.
        var executable = string.IsNullOrWhiteSpace(definition.Executable)
            ? command
            : definition.Executable;

        var path = _executables.Resolve(executable);

        return new EditorState(command, path, ReadProfiles(command), definition);
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

        var definition = editor.Definition!;
        var profile = ProfileFor(config, project, agent);

        // The folder, then the profile only where the editor has somewhere to
        // put it.
        //
        // For the VS Code family that is nowhere, and deliberately so. Asking
        // for a profile stops the folder opening at all: the editor comes up as
        // a window with no folder and no workbench in it, and says nothing
        // about why. It is not the handoff to a running instance, a second
        // instance, the inherited environment, the working directory or the
        // new-window flag — each of those was suspected, and each was cleared
        // by testing it. It is --profile itself, and bisecting the command line
        // separates it cleanly:
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
        // at all, so that editor declares no profile arguments — and the caller
        // says so, because a downgrade nobody mentions is how this went
        // unexplained for so long.
        List<string> arguments = [.. Expand(definition.Arguments, directory, profile)];

        if (profile is { Length: > 0 } && definition.ProfileArguments.Count > 0)
        {
            arguments.AddRange(Expand(definition.ProfileArguments, directory, profile));
        }

        var environment = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (name, value) in definition.Environment)
        {
            environment[name] = Expand(value, directory, profile);
        }

        // Neovim's way of being told: the variable names the configuration
        // directory it loads, so the profile is applied by starting the editor
        // rather than by asking it to switch afterwards.
        if (profile is { Length: > 0 } && definition.ProfileEnvironment is { Length: > 0 } variable)
        {
            environment[variable] = profile;
        }

        var request = new ProcessRequest(
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

            environment.Count == 0 ? null : environment,

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
            RemoveEnvironmentPrefixes: definition.RemoveEnvironmentPrefixes.Count == 0
                ? null
                : definition.RemoveEnvironmentPrefixes);

        // A terminal editor is waited for, because it is drawing on the
        // terminal this process is holding; a windowed one is not, because it
        // outlives the launcher and has no exit code worth having.
        if (definition.Terminal)
        {
            var ran = await _processes.RunInteractiveAsync(request, ct).ConfigureAwait(false);

            return ran.Succeeded
                ? OperationResult.Ok()
                : OperationResult.Fail(
                    ran.Error ?? "The editor could not be started.", ran.ExitCode);
        }

        var result = _processes.StartDetached(request);

        return result.Succeeded
            ? OperationResult.Ok()
            : OperationResult.Fail(result.Error ?? "The editor could not be started.", result.ExitCode);
    }

    /// <summary>Expands the placeholders a definition may carry.</summary>
    /// <remarks>
    /// An unset profile expands to nothing rather than to the literal text, so
    /// a template written for a profile does not put "${PROFILE}" on a command
    /// line when there is none. Nothing here consults the environment: a
    /// template is a template, and reading the process environment through one
    /// would make what an editor is started with depend on where it was started
    /// from.
    /// </remarks>
    private static IEnumerable<string> Expand(
        IEnumerable<string> template,
        string directory,
        string? profile) =>
        template.Select(part => Expand(part, directory, profile));

    private static string Expand(string part, string directory, string? profile) =>
        part
            .Replace("${DIRECTORY}", directory, StringComparison.Ordinal)
            .Replace("${PROFILE}", profile ?? string.Empty, StringComparison.Ordinal);

    /// <summary>
    /// Best-effort read of the editor's profiles. Null on anything unexpected.
    /// </summary>
    private IReadOnlyList<string>? ReadProfiles(string command)
    {
        if (command == "nvim")
        {
            return ReadNeovimProfiles();
        }

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
    /// Neovim's profiles, which are directories rather than entries in a file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>NVIM_APPNAME</c> names the directory under the configuration root
    /// that the editor loads, so every profile is a sibling of the default one
    /// and there is no file listing them. A directory counts as a profile when
    /// it holds an <c>init.lua</c> or an <c>init.vim</c>: the variable will
    /// happily name a directory that holds neither, and Neovim then starts with
    /// no configuration at all, which is not a profile anybody meant to make.
    /// </para>
    /// <para>
    /// The default is left out, exactly as the VS Code reader leaves out its
    /// own: it is what the editor loads when nothing is asked for, and it needs
    /// no naming.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<string>? ReadNeovimProfiles()
    {
        var root = NeovimConfigurationRoot();

        if (root is null || !Directory.Exists(root))
        {
            return null;
        }

        try
        {
            return
            [
                .. Directory.EnumerateDirectories(root)
                    .Where(HoldsNeovimConfiguration)
                    .Select(Path.GetFileName)
                    .Where(name => name is { Length: > 0 } && name != "nvim")
                    .Select(name => name!)
                    .Order(StringComparer.Ordinal),
            ];
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool HoldsNeovimConfiguration(string directory) =>
        File.Exists(Path.Combine(directory, "init.lua"))
        || File.Exists(Path.Combine(directory, "init.vim"));

    /// <summary>
    /// Where Neovim looks for the directory <c>NVIM_APPNAME</c> names.
    /// </summary>
    /// <remarks>
    /// <c>XDG_CONFIG_HOME</c> is honoured where it is set, because somebody who
    /// has moved their configuration has moved their profiles with it, and
    /// reading the default location would report none.
    /// </remarks>
    private static string? NeovimConfigurationRoot()
    {
        if (OperatingSystem.IsWindows())
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            return local.Length == 0 ? null : local;
        }

        var configured = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");

        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return string.IsNullOrEmpty(home) ? null : Path.Combine(home, ".config");
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
