using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Configuration;
using Loadout.Core.Git;
using Loadout.Core.Projects;
using Loadout.Core.Sessions.Hosting;
using Loadout.Core.Statusline;
using Loadout.Core.Usage;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Platform.Abstractions;
using Spectre.Console;
using Spectre.Console.Cli;
using Loadout.Tui;

namespace Loadout.Cli.Commands;

/// <summary>
/// Prints the one line Claude renders at the bottom of its screen.
/// <para>
/// This is the command Claude runs, not one a person types, and that shapes
/// every decision in it. It gets a JSON payload on stdin and must answer on
/// stdout quickly, quietly, and without ever failing: a status line command
/// that errors leaves the bottom of somebody's terminal blank with nothing to
/// explain why, and one that hangs stalls the display. So it has a hard
/// deadline, it swallows everything, and it prints whatever it managed to
/// work out — even if that is only the directory.
/// </para>
/// </summary>
[Description("Render the agent status line. Claude runs this; you do not need to.")]
[CommandMeta(CommandCategory.Integration, Intent = "prompt status bar agent display context")]
public sealed class StatuslineRenderCommand : AsyncCommand<GlobalSettings>
{
    /// <summary>
    /// How long the whole thing may take. Claude redraws the status line
    /// frequently, so a slow answer is worse than a partial one — this bounds
    /// the git call, which is the only part that can block.
    /// </summary>
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How stale the spending figure may get before a refresh is started.
    /// </summary>
    /// <remarks>
    /// Nothing like a live gauge, and it does not need to be: the number moves
    /// over a working day, not over a prompt. The scan behind it takes seconds,
    /// so it happens out of the way rather than on the line's own time.
    /// </remarks>
    private static readonly TimeSpan SpendStaleAfter = TimeSpan.FromMinutes(15);

    private readonly IConfigurationService _configuration;
    private readonly IProjectService _projects;
    private readonly IGitManager _git;
    private readonly ISpendNoticeStore _spend;
    private readonly ILoadedSpecialistStore _loaded;
    private readonly IExecutableResolver _executables;
    private readonly IProcessLauncher _processes;
    private readonly IPlatformPaths _paths;
    private readonly IEnvironmentProvider _environment;
    private readonly TimeProvider _time;

    public StatuslineRenderCommand(
        IConfigurationService configuration,
        IProjectService projects,
        IGitManager git,
        ISpendNoticeStore spend,
        ILoadedSpecialistStore loaded,
        IExecutableResolver executables,
        IProcessLauncher processes,
        IPlatformPaths paths,
        IEnvironmentProvider environment,
        TimeProvider time)
    {
        _configuration = configuration;
        _projects = projects;
        _git = git;
        _spend = spend;
        _loaded = loaded;
        _executables = executables;
        _processes = processes;
        _paths = paths;
        _environment = environment;
        _time = time;
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, GlobalSettings settings, CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(Deadline);

        var payload = StatuslinePayload.Parse(
            await ReadStdinAsync(deadline.Token).ConfigureAwait(false));

        // No payload, or one this build could not read, still leaves a useful
        // line to draw: Claude runs the command inside the session directory,
        // so the working directory alone yields the folder and the branch.
        // An empty status line would be indistinguishable from a broken one.
        payload ??= new StatuslinePayload { Cwd = CurrentDirectory() };

        var options = new Models.Configuration.StatuslineSettings();
        Models.Configuration.LauncherConfig? config = null;

        string? slug = null;
        string? root = null;
        GitRepositoryState? git = null;
        DateTimeOffset? lastCommit = null;

        try
        {
            var configResult = await _configuration.LoadConfigAsync(deadline.Token).ConfigureAwait(false);

            if (configResult.Succeeded)
            {
                config = configResult.Value!;
                options = config.Statusline;
            }

            var directory = payload?.Workspace?.ProjectDir
                ?? payload?.Workspace?.CurrentDir
                ?? payload?.Cwd;

            if (directory is { Length: > 0 })
            {
                // Not being a registered project is entirely normal — somebody
                // may have opened Claude anywhere — so a failure here just
                // drops the segment.
                var resolved = await _projects
                    .ResolveFromDirectoryAsync(directory, deadline.Token)
                    .ConfigureAwait(false);

                if (resolved.Succeeded && resolved.Value is { } project)
                {
                    slug = project.Entry.Slug;
                    root = project.LocalPath;
                }

                (git, lastCommit) = await ReadGitAsync(
                    _git, directory, options, deadline.Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || deadline.IsCancellationRequested)
        {
            // Deliberately broad. Whatever went wrong, the remedy is the same:
            // print what is known. There is nowhere to report it to.
        }

        SpendNotice? spend = null;

        if (options.ShowSpend && slug is { Length: > 0 })
        {
            try
            {
                spend = await _spend.ReadAsync(slug, deadline.Token).ConfigureAwait(false);

                StartRefreshIfDue(slug, spend);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || deadline.IsCancellationRequested)
            {
                // A cache that cannot be read drops the segment. Nothing about
                // spending is worth a prompt failing to draw.
            }
        }

        LoadedSpecialists? loaded = null;

        if (options.ShowSpecialists && slug is { Length: > 0 })
        {
            try
            {
                loaded = await _loaded.ReadAsync(slug, deadline.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || deadline.IsCancellationRequested)
            {
                // Drops the segment. Nothing here is worth a prompt not drawing.
            }
        }

        DateTimeOffset? now = null;
        StatuslineTimes? times = null;
        var fixedTimes = false;

        try
        {
            fixedTimes = config is not null && StatuslineInstaller.RedrawsNever(
                Loadout.Core.Instructions.AccessibilityProfile.Resolve(config.Accessibility).Display.Redraw);

            var at = _time.GetUtcNow();
            var studio = _environment.GetVariable(SessionSignals.Variable);

            // The transcript is read synchronously, so it is read off this
            // thread: the deadline then bounds it like everything else.
            times = await Task.Run(
                    () => ReadTimes(options, payload, _paths.Paths.State, studio, at, lastCommit),
                    deadline.Token)
                .WaitAsync(deadline.Token)
                .ConfigureAwait(false);

            now = at;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || deadline.IsCancellationRequested)
        {
            // Drops the time group, and only that. With no moment to draw for,
            // the renderer leaves every time segment out.
        }

        var line = StatuslineRenderer.Render(
            new StatuslineInputs(
                payload, slug, root, git, spend, loaded,
                Now: now,
                Times: times,
                FixedTimes: fixedTimes,
                Width: Columns(_environment.GetVariable("COLUMNS"))),
            options);

        Console.Out.WriteLine(line);

        return (int)ExitCode.Success;
    }

    /// <summary>
    /// Asks git for the branch and, only when its segment is on, the head
    /// commit's time, the two at once.
    /// </summary>
    /// <remarks>
    /// The commit time is a git process of its own, since the status that
    /// gives the branch carries no time. It is off by default, so by default
    /// the line starts no more processes than it did before it existed.
    /// </remarks>
    internal static async Task<(GitRepositoryState? State, DateTimeOffset? LastCommit)> ReadGitAsync(
        IGitManager git,
        string directory,
        Models.Configuration.StatuslineSettings options,
        CancellationToken ct)
    {
        var stateTask = git.GetStateAsync(directory, ct);
        var commitTask = options.ShowLastCommit
            ? git.GetHeadCommitTimeAsync(directory, ct)
            : Task.FromResult(Models.Results.OperationResult<DateTimeOffset?>.Ok(null));

        await Task.WhenAll(stateTask, commitTask).ConfigureAwait(false);

        return ((await stateTask.ConfigureAwait(false)).Value, (await commitTask.ConfigureAwait(false)).Value);
    }

    /// <summary>Gathers the session's times for the line, reading the transcript only when a segment needs it.</summary>
    /// <param name="options">Which segments are on.</param>
    /// <param name="payload">What Claude sent, which names the transcript and carries the session's length.</param>
    /// <param name="state">The launcher's state directory, where the cache and Studio's signals live.</param>
    /// <param name="studioSession">The Studio session this runs inside, if any.</param>
    /// <param name="now">The moment the line is drawn for.</param>
    /// <param name="lastCommit">The head commit's time, when it was asked for.</param>
    internal static StatuslineTimes ReadTimes(
        Models.Configuration.StatuslineSettings options,
        StatuslinePayload? payload,
        string state,
        string? studioSession,
        DateTimeOffset now,
        DateTimeOffset? lastCommit)
    {
        var reading = options.ShowWaiting || options.ShowLastWrite || options.ShowTurn
            ? TranscriptCache.Read(state, payload?.TranscriptFile, SessionSignals.BackgroundLimit)
            : null;

        // Only Studio sees a permission prompt, and only a signal written
        // since the transcript last moved still speaks for it: an approval
        // already given leaves the file behind until the next hook replaces it.
        SessionSignal? signal = null;

        if (reading?.PendingToolSince is not null && SessionSignals.IsSessionId(studioSession))
        {
            var read = SessionSignals.Read(SessionSignals.Directory(state), studioSession!);

            if (TranscriptReading.SignalCounts(read, reading.LastEntryAt))
            {
                signal = read;
            }
        }

        var permission = signal is { State: "approval" };

        return new StatuslineTimes(
            TurnStartedAt: reading?.TurnStartedAt,
            TurnEndedAt: reading?.TurnEndedAt,
            PendingToolSince: reading?.PendingToolSince,
            PermissionAsked: permission,
            PermissionAt: permission ? signal!.At : null,
            BackgroundOutstanding: reading?.Outstanding(now) ?? false,
            WakeupDue: reading?.WakeupDue,
            LastWriteAt: reading?.LastWriteAt,
            SessionLength: payload?.Cost?.TotalDurationMs is { } ms ? TimeSpan.FromMilliseconds(ms) : null,
            LastCommitAt: lastCommit);
    }

    /// <summary>
    /// The terminal's width from <c>COLUMNS</c>, or null. Claude Code does not
    /// put the width in the payload, so this is the only place to learn it.
    /// </summary>
    internal static int? Columns(string? value) =>
        int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var columns)
            && columns > 0
            ? columns
            : null;

    /// <summary>
    /// Starts a refresh of the spending figure, out of the way, when it is due.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Detached and not waited for. The whole point is that the line draws now
    /// and the number catches up; waiting would put the two-second scan back on
    /// the prompt, which is what the cache exists to avoid.
    /// </para>
    /// <para>
    /// Claiming the refresh is what stops a stampede. This runs several times a
    /// minute, and without the claim every one of those would see the same
    /// stale file and start its own scan.
    /// </para>
    /// </remarks>
    private void StartRefreshIfDue(string slug, SpendNotice? notice)
    {
        var now = DateTimeOffset.UtcNow;

        if (notice is not null && now - notice.ComputedUtc < SpendStaleAfter)
        {
            return;
        }

        if (!_spend.ClaimRefresh(slug, now, SpendStaleAfter))
        {
            return;
        }

        if (Environment.ProcessPath is not { Length: > 0 } self
            && _executables.Resolve("loadout") is not { Length: > 0 })
        {
            return;
        }

        var executable = Environment.ProcessPath is { Length: > 0 } path
            ? path
            : _executables.Resolve("loadout")!;

        _processes.StartDetached(new ProcessRequest(
            executable,
            ["spend", "refresh", "--project", slug]));
    }

    /// <summary>The working directory, or null on the rare systems that refuse to say.</summary>
    private static string? CurrentDirectory()
    {
        try
        {
            return Directory.GetCurrentDirectory();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the payload, tolerating not being given one. Run by hand from a
    /// terminal there is no stdin to read, and blocking forever waiting for it
    /// would look like a hang.
    /// </summary>
    private static async Task<string?> ReadStdinAsync(CancellationToken ct)
    {
        try
        {
            if (Console.IsInputRedirected)
            {
                return await Console.In.ReadToEndAsync(ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
            // No payload; the line is drawn from what the launcher knows.
        }

        return null;
    }
}

/// <summary>Which settings file a status line command applies to.</summary>
public class StatuslineTargetSettings : GlobalSettings
{
    [CommandOption("--project <SLUG>")]
    [Description("Apply to this registered project instead of the current directory.")]
    public string? Project { get; init; }

    [CommandOption("--all")]
    [Description("Apply to every registered project.")]
    public bool All { get; init; }

    [CommandOption("--global")]
    [Description("Apply to the Claude user settings, so it shows in every session on this machine.")]
    public bool Global { get; init; }
}

/// <summary>
/// Adds the status line to a Claude settings file (spec sections 9 and 31).
/// <para>
/// Two scopes, which do genuinely different things. A project install writes
/// into the workspace repository, so it travels to every machine that clones
/// it and applies only when the launcher starts the session. A global install
/// writes to the Claude user settings, so it applies to every session on this
/// machine including ones started by hand — at the cost of being machine-local.
/// </para>
/// </summary>
[Description("Show the project, directory, branch and context usage in the agent status line.")]
[CommandMeta(CommandCategory.Integration, Intent = "status line install prompt project branch context", Mutates = true)]
public sealed class StatuslineInstallCommand : AsyncCommand<StatuslineTargetSettings>
{
    /// <summary>
    /// How long <c>claude --version</c> may take. Only the version line is
    /// wanted, and an install that hangs on it would be worse than one that
    /// leaves the interval out.
    /// </summary>
    private static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(10);

    private readonly StatuslineTargets _targets;
    private readonly IConfigurationService _configuration;
    private readonly IExecutableResolver _executables;
    private readonly IProcessLauncher _processes;
    private readonly IAnsiConsole _console;

    public StatuslineInstallCommand(
        StatuslineTargets targets,
        IConfigurationService configuration,
        IExecutableResolver executables,
        IProcessLauncher processes,
        IAnsiConsole console)
    {
        _targets = targets;
        _configuration = configuration;
        _executables = executables;
        _processes = processes;
        _console = console;
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, StatuslineTargetSettings settings, CancellationToken cancellationToken)
    {
        var output = new CommandOutput(_console, settings);

        var targetsResult = await _targets.ResolveAsync(settings).ConfigureAwait(false);

        if (targetsResult.Failed)
        {
            return output.Fail(targetsResult);
        }

        var executable = StatuslineTargets.ExecutablePath();

        if (executable is null)
        {
            return output.Fail(
                "Could not work out where this launcher is installed, so there is no command to install.",
                ExitCode.GeneralFailure);
        }

        // A config that cannot be read is no reason to refuse the install; the
        // default profile redraws, so it is treated as that.
        var configResult = await _configuration.LoadConfigAsync(cancellationToken).ConfigureAwait(false);
        var redraw = configResult.Succeeded
            ? Loadout.Core.Instructions.AccessibilityProfile
                .Resolve(configResult.Value!.Accessibility).Display.Redraw
            : null;

        var version = await ReadClaudeVersionAsync(cancellationToken).ConfigureAwait(false);
        var refresh = StatuslineInstaller.RefreshFor(version, redraw);
        var outcome = RefreshOutcome(refresh, version, redraw);

        // The targets are resolved, so the preview can name every settings file it
        // would edit — and editing an agent's own settings is worth seeing first.
        if (settings.DryRun)
        {
            foreach (var target in targetsResult.Value!)
            {
                output.WriteLine(
                    $"[bold]Would install[/] for {target.Description.EscapeMarkup()}");
                output.WriteLine($"  [dim]{target.SettingsPath.EscapeMarkup()}[/]");
            }

            output.WriteLine(refresh is { } seconds
                ? $"It would write refreshInterval {seconds}, so it {outcome.EscapeMarkup()}."
                : $"It would write no refreshInterval, so it {outcome.EscapeMarkup()}.");
            output.WriteLine("Nothing was changed.");

            return CommandOutput.Success();
        }
        var installed = new List<StatuslineInstallation>();

        foreach (var target in targetsResult.Value!)
        {
            var result = await StatuslineInstaller
                .InstallAsync(target.SettingsPath, executable, refresh)
                .ConfigureAwait(false);

            if (result.Failed)
            {
                return output.Fail(result);
            }

            installed.Add(result.Value!);
        }

        if (output.IsJson)
        {
            output.WriteJson(installed.Select(i => new
            {
                path = i.Path,
                command = i.Command,
                replaced = i.Replaced,
                refresh = i.Refresh,
            }));

            return CommandOutput.Success();
        }

        foreach (var (target, installation) in targetsResult.Value!.Zip(installed))
        {
            output.WriteLine($"[green]Installed[/] for {target.Description.EscapeMarkup()}");
            output.WriteLine($"  [dim]{installation.Path.EscapeMarkup()}[/]");

            if (installation.Replaced is { Length: > 0 } previous
                && previous != installation.Command)
            {
                // Silently discarding somebody's own status line would be the
                // kind of thing found out weeks later.
                output.WriteLine(
                    $"  [yellow]Replaced[/] [dim]{previous.EscapeMarkup()}[/]");
            }
        }

        output.WriteLine($"The line {outcome.EscapeMarkup()}.");
        output.WriteBlankLine();
        output.WriteLine("[dim]Start a new session to see it. Claude reads settings at startup.[/]");

        return CommandOutput.Success();
    }

    /// <summary>
    /// Which of the three things an install did about ticking, in words: it
    /// matters, because a line that does not tick looks stuck rather than off.
    /// </summary>
    internal static string RefreshOutcome(int? refresh, string? version, string? redraw) =>
        refresh is { } seconds ? $"ticks every {seconds}s"
        : StatuslineInstaller.RedrawsNever(redraw) ? "has no ticking (screen-reader profile)"
        : version is { Length: > 0 }
            ? $"updates on redraw only (Claude Code {version} not known to support a refresh interval)"
            : "updates on redraw only (Claude Code's version could not be read, so it is not known to support a refresh interval)";

    /// <summary>
    /// The first line of <c>claude --version</c>, or null when Claude Code is
    /// not on the path or does not answer in time.
    /// </summary>
    private async Task<string?> ReadClaudeVersionAsync(CancellationToken ct)
    {
        if (_executables.Resolve("claude") is not { Length: > 0 } claude)
        {
            return null;
        }

        var result = await _processes.RunAsync(
            new ProcessRequest(claude, ["--version"]),
            VersionTimeout,
            ct).ConfigureAwait(false);

        if (result.Failed || result.Value is null || !result.Value.Succeeded)
        {
            return null;
        }

        return result.Value.StandardOutput.Trim().Split('\n').FirstOrDefault()?.Trim();
    }
}

/// <summary>Removes the status line, leaving the rest of the settings file alone.</summary>
[Description("Remove the agent status line.")]
[CommandMeta(CommandCategory.Integration, Intent = "status line remove uninstall", Mutates = true)]
public sealed class StatuslineUninstallCommand : AsyncCommand<StatuslineTargetSettings>
{
    private readonly StatuslineTargets _targets;
    private readonly IAnsiConsole _console;

    public StatuslineUninstallCommand(StatuslineTargets targets, IAnsiConsole console)
    {
        _targets = targets;
        _console = console;
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, StatuslineTargetSettings settings, CancellationToken cancellationToken)
    {
        var output = new CommandOutput(_console, settings);

        var targetsResult = await _targets.ResolveAsync(settings).ConfigureAwait(false);

        if (targetsResult.Failed)
        {
            return output.Fail(targetsResult);
        }

        // Same for removal: name the files rather than edit them.
        if (settings.DryRun)
        {
            foreach (var target in targetsResult.Value!)
            {
                output.WriteLine(
                    $"[bold]Would remove[/] from {target.Description.EscapeMarkup()}");
            }

            output.WriteLine("Nothing was changed.");
        
            return CommandOutput.Success();
        }
        foreach (var target in targetsResult.Value!)
        {
            var result = await StatuslineInstaller
                .UninstallAsync(target.SettingsPath)
                .ConfigureAwait(false);

            if (result.Failed)
            {
                return output.Fail(result);
            }

            output.WriteLine(result.Value
                ? $"[green]Removed[/] from {target.Description.EscapeMarkup()}"
                : $"[dim]Nothing to remove for {target.Description.EscapeMarkup()}[/]");
        }

        return CommandOutput.Success();
    }
}

/// <summary>
/// Says where the status line is installed and what it currently looks like.
/// <para>
/// The preview matters more than it sounds: the line is otherwise only visible
/// by starting an agent session, which is a slow way to find out that a segment
/// is switched off.
/// </para>
/// </summary>
[Description("Show where the status line is installed and what it looks like.")]
[CommandMeta(CommandCategory.Integration, Intent = "status line show where installed preview")]
public sealed class StatuslineShowCommand : AsyncCommand<StatuslineTargetSettings>
{
    private readonly StatuslineTargets _targets;
    private readonly IConfigurationService _configuration;
    private readonly IProjectService _projects;
    private readonly IGitManager _git;
    private readonly IAnsiConsole _console;
    private readonly TimeProvider _time;

    public StatuslineShowCommand(
        StatuslineTargets targets,
        IConfigurationService configuration,
        IProjectService projects,
        IGitManager git,
        IAnsiConsole console,
        TimeProvider time)
    {
        _targets = targets;
        _configuration = configuration;
        _projects = projects;
        _git = git;
        _console = console;
        _time = time;
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, StatuslineTargetSettings settings, CancellationToken cancellationToken)
    {
        var output = new CommandOutput(_console, settings);

        var configResult = await _configuration.LoadConfigAsync().ConfigureAwait(false);

        if (configResult.Failed)
        {
            return output.Fail(configResult);
        }

        var options = configResult.Value!.Statusline;

        var targetsResult = await _targets.ResolveAsync(settings).ConfigureAwait(false);

        // Unlike installing, showing changes nothing, so being outside a
        // registered project is no reason to refuse. Fall back to the
        // machine-wide file, which is the one that would apply here anyway.
        var targets = targetsResult.Succeeded
            ? targetsResult.Value!
            : [new StatuslineTarget("every session on this machine", _targets.GlobalSettingsPath())];

        var directory = settings.Repo ?? Directory.GetCurrentDirectory();

        var resolved = await _projects.ResolveFromDirectoryAsync(directory).ConfigureAwait(false);
        var state = await _git.GetStateAsync(directory).ConfigureAwait(false);

        // A payload the way Claude would send one, so the preview shows the
        // model and context segments rather than quietly omitting them.
        var payload = new StatuslinePayload
        {
            Cwd = directory,
            Workspace = new StatuslineWorkspace
            {
                CurrentDir = directory,
                ProjectDir = resolved.Value?.LocalPath ?? state.Value?.Root,
            },
            Model = new StatuslineModel { DisplayName = "Opus 5" },
            ContextWindow = new StatuslineContextWindow
            {
                TotalInputTokens = 84_000,
                ContextWindowSize = 200_000,
            },
        };

        // Sample times as well, so the preview shows the time segments that
        // are on rather than leaving them out for want of a transcript.
        var now = _time.GetUtcNow();
        var redrawNever = StatuslineInstaller.RedrawsNever(
            Loadout.Core.Instructions.AccessibilityProfile
                .Resolve(configResult.Value!.Accessibility).Display.Redraw);

        var inputs = new StatuslineInputs(
            payload,
            resolved.Value?.Entry.Slug,
            resolved.Value?.LocalPath,
            state.Value,
            Now: now,
            Times: PreviewTimes(now),
            FixedTimes: redrawNever);

        var preview = StatuslineRenderer.Render(inputs, options);

        var installations = new List<(string Description, string Path, string? Command, int? Refresh)>();

        foreach (var target in targets)
        {
            var command = await StatuslineInstaller
                .ReadCommandAsync(target.SettingsPath)
                .ConfigureAwait(false);

            var refresh = await StatuslineInstaller
                .ReadRefreshAsync(target.SettingsPath)
                .ConfigureAwait(false);

            installations.Add((target.Description, target.SettingsPath, command.Value, refresh));
        }

        // An entry written before the interval existed still draws, but its
        // relative times stop moving while the session is idle. Under a profile
        // that asks for nothing to redraw, no interval is the right answer.

        // What an install would write today. A recorded command that differs is
        // not merely out of date: it may not run at all. Claude reports nothing
        // when the status line command fails — the line simply does not appear
        // — so "installed" said of a command that cannot execute is exactly the
        // report that hid a shell-quoting defect through a whole release.
        var expected = StatuslineTargets.ExecutablePath() is { } current
            ? StatuslineInstaller.CommandFor(current)
            : null;

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                // JSON is read by programs, and escape codes in a string
                // field are noise to every one of them. A whole copy, so a
                // segment added later is not quietly left out of the preview.
                preview = StatuslineRenderer.Render(inputs, Uncoloured(options)),
                segments = new
                {
                    project = options.ShowProject,
                    directory = options.ShowDirectory,
                    git = options.ShowGit,
                    model = options.ShowModel,
                    context = options.ShowContext,
                    waiting = options.ShowWaiting,
                    lastWrite = options.ShowLastWrite,
                    clock = options.ShowClock,
                    session = options.ShowSession,
                    turn = options.ShowTurn,
                    lastCommit = options.ShowLastCommit,
                },
                installed = installations.Select(i => new
                {
                    scope = i.Description,
                    path = i.Path,
                    command = i.Command,

                    // Null when nothing is installed, or when this launcher
                    // cannot tell where it lives and so has nothing to compare.
                    matchesCurrent = i.Command is null || expected is null
                        ? (bool?)null
                        : i.Command == expected,

                    // Seconds between redraws while idle, or null for none.
                    refresh = i.Refresh,
                }),
            });

            return CommandOutput.Success();
        }

        output.WriteLine("[bold]Preview[/]");

        // Written raw: the line carries its own escape codes, and putting it
        // through markup rendering would print them rather than apply them.
        _console.WriteLine();
        Console.Out.WriteLine("  " + preview);
        _console.WriteLine();

        output.WriteLine("[bold]Segments[/]");
        output.WriteLine($"  project    {OnOff(options.ShowProject)}");
        output.WriteLine($"  directory  {OnOff(options.ShowDirectory)}");
        output.WriteLine($"  git        {OnOff(options.ShowGit)}");
        output.WriteLine($"  model      {OnOff(options.ShowModel)}");
        output.WriteLine($"  context    {OnOff(options.ShowContext)}");
        output.WriteLine($"  waiting    {OnOff(options.ShowWaiting)}");
        output.WriteLine($"  write      {OnOff(options.ShowLastWrite)}");
        output.WriteLine($"  clock      {OnOff(options.ShowClock)}");
        output.WriteLine($"  session    {OnOff(options.ShowSession)}");
        output.WriteLine($"  turn       {OnOff(options.ShowTurn)}");
        output.WriteLine($"  commit     {OnOff(options.ShowLastCommit)}");
        output.WriteBlankLine();
        output.WriteLine("[dim]Change these with loadout config set statusline-git false[/]");
        output.WriteBlankLine();

        output.WriteLine("[bold]Installed[/]");

        var stale = false;
        var still = false;

        foreach (var (description, path, command, refresh) in installations)
        {
            still |= command is not null && refresh is null && !redrawNever;

            var matches = command is null || expected is null || command == expected;

            stale |= !matches;

            output.WriteLine(command switch
            {
                null => $"  [dim]not installed[/]  {description.EscapeMarkup()}",
                _ when !matches => $"  [yellow]needs repair[/]   {description.EscapeMarkup()}",
                _ => $"  [green]installed[/]      {description.EscapeMarkup()}",
            });

            output.WriteLine($"    [dim]{path.EscapeMarkup()}[/]");

            // Both strings, because the difference is often a single character
            // and describing it in prose would be worse than showing it.
            if (!matches)
            {
                output.WriteLine($"    [dim]recorded {command!.EscapeMarkup()}[/]");
                output.WriteLine($"    [dim]expected {expected!.EscapeMarkup()}[/]");
            }
        }

        if (stale)
        {
            output.WriteBlankLine();
            output.WriteLine("[yellow]Repair it with loadout statusline install[/]");
        }

        if (still)
        {
            output.WriteBlankLine();
            output.WriteLine(
                "[yellow]Reinstall with loadout statusline install so relative times keep moving while the session is idle[/]");
        }

        if (installations.TrueForAll(i => i.Command is null))
        {
            output.WriteBlankLine();
            output.WriteLine("[dim]Turn it on with loadout statusline install[/]");
        }

        return CommandOutput.Success();
    }

    private static string OnOff(bool value) => value ? "[green]on[/]" : "[dim]off[/]";

    /// <summary>The settings as they are, with colour off.</summary>
    internal static Models.Configuration.StatuslineSettings Uncoloured(Models.Configuration.StatuslineSettings options)
    {
        var copy = options.Copy();

        copy.Colour = false;

        return copy;
    }

    /// <summary>
    /// Times for the preview, as a session that finished its turn a few
    /// minutes ago would have them.
    /// </summary>
    internal static StatuslineTimes PreviewTimes(DateTimeOffset now) => new(
        TurnStartedAt: now.AddMinutes(-6).AddSeconds(-14),
        TurnEndedAt: now.AddMinutes(-4),
        LastWriteAt: now.AddMinutes(-5),
        SessionLength: TimeSpan.FromMinutes(72),
        LastCommitAt: now.AddHours(-3));
}
