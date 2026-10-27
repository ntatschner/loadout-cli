using System.Text.Json;
using System.Text.Json.Nodes;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Statusline;

/// <summary>
/// Finds and runs the person's own status line in a session Studio hosts,
/// where the launcher's line has taken its place.
/// </summary>
/// <remarks>
/// <para>
/// A hosted session is handed the launcher's status line through
/// <c>--settings</c>, which outranks every settings file the person keeps, so
/// their own line would simply stop. With <c>statusline-in-studio full</c>
/// the launcher runs it for them, with the payload Claude gave, and prints
/// what it printed: their line keeps working and Studio still gets the data.
/// </para>
/// <para>
/// The launcher's own line, installed earlier with <c>loadout statusline
/// install</c>, is recognised and not run as a command: the launcher draws it
/// in the same process instead, which costs nothing and cannot recurse.
/// </para>
/// </remarks>
public static class StatuslineWrap
{
    private static readonly JsonDocumentOptions Lenient = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// The settings files that could have given the session its line, highest
    /// precedence first, as Claude ranks them below its command-line settings.
    /// </summary>
    /// <param name="workspaceSettings">The project's settings in the launcher's workspace, which the launch passes with <c>--settings</c>, or null.</param>
    /// <param name="projectDirectory">The repository the session was opened in, or null.</param>
    /// <param name="userSettings">The Claude user settings file.</param>
    public static IReadOnlyList<string> Sources(string? workspaceSettings, string? projectDirectory, string userSettings)
    {
        var sources = new List<string>();

        if (workspaceSettings is { Length: > 0 })
        {
            sources.Add(workspaceSettings);
        }

        if (projectDirectory is { Length: > 0 })
        {
            sources.Add(Path.Combine(projectDirectory, ".claude", "settings.local.json"));
            sources.Add(Path.Combine(projectDirectory, ".claude", "settings.json"));
        }

        sources.Add(userSettings);

        return sources;
    }

    /// <summary>
    /// The person's own status line command: the one in the highest file that
    /// sets one, or null where none does or that one is the launcher's own.
    /// </summary>
    public static string? FindOwnCommand(IEnumerable<string> sources)
    {
        foreach (var source in sources)
        {
            if (Command(source) is not { } command)
            {
                continue;
            }

            // The one Claude would have used. Being the launcher's own, it is
            // drawn in-process rather than started again.
            return IsLaunchers(command) ? null : command;
        }

        return null;
    }

    /// <summary>
    /// Whether a status line command is this launcher's, whether as installed
    /// or as the line a hosted session is given.
    /// </summary>
    public static bool IsLaunchers(string command)
    {
        var trimmed = command.Trim();

        return trimmed.Contains("loadout", StringComparison.OrdinalIgnoreCase)
            && (trimmed.EndsWith(" statusline", StringComparison.Ordinal)
                || trimmed.Contains(" statusline --studio", StringComparison.Ordinal));
    }

    /// <summary>
    /// The shell a status line command is run through, the way Claude runs
    /// one: Git Bash on Windows, <c>/bin/sh</c> elsewhere. Null where there is none.
    /// </summary>
    public static string? Shell(IEnvironmentProvider environment, IExecutableResolver executables, bool windows)
    {
        if (!windows)
        {
            return "/bin/sh";
        }

        // Claude's own override for where Git Bash is, honoured the same way.
        return environment.GetVariable("CLAUDE_CODE_GIT_BASH_PATH") is { Length: > 0 } bash && File.Exists(bash)
            ? bash
            : executables.Resolve("bash");
    }

    /// <summary>
    /// Runs the person's command with the payload on its stdin and returns
    /// what it printed, or null where it failed to start, timed out, or
    /// exited non-zero having printed nothing.
    /// </summary>
    public static async Task<string?> RunAsync(
        IProcessLauncher processes,
        string shell,
        string command,
        string? payload,
        string? workingDirectory,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var result = await processes.RunAsync(
            new ProcessRequest(shell, ["-c", command], workingDirectory, StandardInput: payload ?? string.Empty),
            timeout,
            ct).ConfigureAwait(false);

        if (result.Failed || result.Value is not { } outcome)
        {
            return null;
        }

        // A line that failed but still said something is shown, as Claude
        // would; one that said nothing leaves the row empty.
        var printed = outcome.StandardOutput.TrimEnd('\r', '\n');

        return printed.Length > 0 ? printed : null;
    }

    private static string? Command(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var node = JsonNode.Parse(File.ReadAllText(path), documentOptions: Lenient);

            return node?["statusLine"]?["command"] is JsonValue value
                && value.TryGetValue<string>(out var command)
                && command.Trim().Length > 0
                    ? command
                    : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }
}
