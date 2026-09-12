using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Instructions;
using Loadout.Core.Projects;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>
/// Puts what the project already knows in front of a session, before it is
/// asked to work it out again.
/// </summary>
/// <remarks>
/// <para>
/// Meant for <c>--hook</c> rather than for a person: Claude runs it as a
/// <c>UserPromptSubmit</c> hook with the prompt on stdin, and anything it
/// writes to stdout as <c>additionalContext</c> reaches the session before it
/// starts. Without the flag it takes the question as an argument, which is
/// there so the hook's behaviour can be tried by hand rather than only observed
/// in a running session.
/// </para>
/// <para>
/// Silence is the normal answer and not a failure. Every prompt runs this,
/// including the ones that are not questions about the project at all.
/// </para>
/// </remarks>
[Description("Put what the project already knows in front of a session.")]
[CommandMeta(CommandCategory.AgentConfiguration, Intent = "memory recall hook prompt context inject")]
public sealed class MemoryRecallCommand : AsyncCommand<MemoryRecallCommand.Settings>
{
    private readonly IMemoryService _memory;
    private readonly IProjectService _projects;
    private readonly IWorkspaceManager _workspace;
    private readonly IAnsiConsole _console;

    public MemoryRecallCommand(
        IMemoryService memory,
        IProjectService projects,
        IWorkspaceManager workspace,
        IAnsiConsole console)
    {
        _memory = memory;
        _projects = projects;
        _workspace = workspace;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "[query]")]
        [Description("What is being asked. Read from the hook's input when --hook is given.")]
        public string? Query { get; init; }

        [CommandOption("--project <SLUG>")]
        [Description("Project to search. Defaults to the repository you are in.")]
        public string? Project { get; init; }

        [CommandOption("--hook")]
        [Description("Read the prompt from the agent's hook input on stdin.")]
        public bool Hook { get; init; }
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        var query = settings.Hook
            ? RecallHook.Parse(await Console.In.ReadToEndAsync(cancellationToken).ConfigureAwait(false))
            : settings.Query;

        if (string.IsNullOrWhiteSpace(query))
        {
            // Nothing to search for. In hook mode that is an ordinary outcome
            // and writing an error would put it in front of the session, so it
            // succeeds silently; asked directly, it is a mistake worth naming.
            return settings.Hook
                ? CommandOutput.Success()
                : output.Fail("Say what to recall.", ExitCode.InvalidArguments);
        }

        var resolution = settings.Project is not null
            ? await _projects.ResolveAsync(settings.Project, cancellationToken).ConfigureAwait(false)
            : await _projects.ResolveFromDirectoryAsync(
                settings.Repo ?? Directory.GetCurrentDirectory(), cancellationToken).ConfigureAwait(false);

        // A hook that cannot work out the project says nothing. It runs in
        // whatever directory the session was started in, which is not always
        // one the launcher knows, and an error there would reach the session.
        if (resolution.Failed)
        {
            return settings.Hook ? CommandOutput.Success() : output.Fail(resolution);
        }

        var listed = await _memory
            .ListAsync(_workspace.LocalPath, resolution.Value!.Entry.Slug, cancellationToken)
            .ConfigureAwait(false);

        if (listed.Failed)
        {
            return settings.Hook ? CommandOutput.Success() : output.Fail(listed);
        }

        var matches = MemorySearch.Rank(listed.Value!, query);

        if (settings.Hook)
        {
            if (RecallHook.Output(matches) is { } document)
            {
                await System.Console.Out.WriteAsync(document).ConfigureAwait(false);
            }

            return CommandOutput.Success();
        }

        var spoken = RecallHook.Context(matches);

        if (spoken is null)
        {
            output.WriteLine("[dim]Nothing recorded here bears on that.[/]");

            return CommandOutput.Success();
        }

        output.WriteLine(Markup.Escape(spoken));

        return CommandOutput.Success();
    }
}
