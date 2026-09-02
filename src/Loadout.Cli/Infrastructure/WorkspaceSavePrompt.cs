using Loadout.Agents;
using Loadout.Core.Workspace;
using Spectre.Console;

namespace Loadout.Cli.Infrastructure;

/// <summary>
/// Asks what to do with workspace changes when a session ends
/// (spec section 45).
/// <para>
/// The question lives here rather than in core because core must never prompt:
/// spec section 37 forbids a menu appearing in a pipe or a CI job. Core decides
/// whether a decision is needed; this decides how to ask for it.
/// </para>
/// </summary>
public sealed class WorkspaceSavePrompt
{
    private readonly IAnsiConsole _console;
    private readonly IWorkspaceManager _workspace;

    public WorkspaceSavePrompt(IAnsiConsole console, IWorkspaceManager workspace)
    {
        _console = console;
        _workspace = workspace;
    }

    /// <summary>
    /// Offers the four choices of spec section 45 when the session left
    /// uncommitted workspace changes and the caller can hold a conversation.
    /// </summary>
    public async Task HandleAsync(
        LaunchOutcome outcome,
        GlobalSettings settings,
        CancellationToken ct = default)
    {
        var pending = outcome.PendingWorkspaceChanges;

        if (pending is null || pending.Count == 0)
        {
            return;
        }

        if (!settings.AllowsPrompting)
        {
            // Nobody can answer, so the changes are left exactly as they are and
            // the user is told where to find them. Committing on their behalf
            // would be deciding for them.
            _console.MarkupLine(
                $"[yellow]{pending.Count} workspace file(s) changed and were left uncommitted.[/]");

            _console.MarkupLine("[dim]Save them with:[/] loadout workspace save");

            return;
        }

        _console.WriteLine();
        _console.MarkupLine($"[bold]{pending.Count} workspace file(s) changed[/]");

        foreach (var path in pending.Take(10))
        {
            _console.MarkupLine($"  {Markup.Escape(path)}");
        }

        if (pending.Count > 10)
        {
            _console.MarkupLine($"  [dim]and {pending.Count - 10} more[/]");
        }

        _console.WriteLine();

        const string SaveAndSync = "Save and sync";
        const string SaveLocally = "Save locally";
        const string Review = "Review the changes";
        const string Leave = "Leave them uncommitted";

        var choice = _console.Prompt(
            new SelectionPrompt<string>()
                .Title("What would you like to do with them?")
                .AddChoices(SaveAndSync, SaveLocally, Review, Leave));

        if (choice == Review)
        {
            // Reviewing does not resolve anything, so the question comes back
            // rather than the changes being silently left behind.
            _console.MarkupLine($"[dim]Run:[/] git -C {_workspace.LocalPath} diff");
            _console.WriteLine();

            await HandleAsync(outcome, settings, ct).ConfigureAwait(false);

            return;
        }

        if (choice == Leave)
        {
            // Deliberately not "discard". Spec section 47's rule against data
            // loss applies here too: the launcher has no business deleting work
            // somebody just did, so the changes stay on disk.
            _console.MarkupLine(
                "[dim]Left uncommitted. Save later with:[/] loadout workspace save");

            return;
        }

        var result = await _workspace.SaveAsync(
            outcome.ProjectName ?? "workspace",
            outcome.AgentName ?? "agent",
            push: choice == SaveAndSync,
            ct).ConfigureAwait(false);

        if (result.Failed)
        {
            _console.MarkupLine($"[yellow]{Loadout.Tui.Shown.Safely(result.Error!)}[/]");
            return;
        }

        _console.MarkupLine(choice == SaveAndSync
            ? "[green]Saved and pushed.[/]"
            : "[green]Saved locally.[/]");
    }
}
