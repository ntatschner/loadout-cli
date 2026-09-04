using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Packs;
using Loadout.Core.Security;
using Loadout.Models;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>
/// Lists the declared packs and where each stands on this machine.
/// </summary>
/// <remarks>
/// The list is the security surface, so it says the standing of every pack
/// rather than only the ones that load. A pack sitting unapproved is the
/// ordinary state after somebody else declares one, and it has to be visible
/// or the answer to "why is nothing happening" is invisible too.
/// </remarks>
[Description("List the specialist packs, and whether this machine has approved each.")]
[CommandMeta(CommandCategory.AgentConfiguration,
    Intent = "packs specialist packs house standards shared specialists")]
public sealed class PackListCommand : AsyncCommand<GlobalSettings>
{
    private readonly IPackService _packs;
    private readonly IAnsiConsole _console;

    public PackListCommand(IPackService packs, IAnsiConsole console)
    {
        _packs = packs;
        _console = console;
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        GlobalSettings settings,
        CancellationToken cancellationToken)
    {
        var output = new CommandOutput(_console, settings);

        var standing = await _packs.StandingAsync(cancellationToken).ConfigureAwait(false);

        if (standing.Failed)
        {
            return output.Fail(standing);
        }

        if (output.IsJson)
        {
            output.WriteJson(standing.Value!.Select(entry => new
            {
                entry.Pack.Name,
                // Redacted here too. A remote can carry a credential in its
                // userinfo, and JSON is piped into logs and files at least as
                // often as it is read by a person.
                remote = SecretRedactor.Redact(entry.Pack.Remote),
                reference = entry.Pack.Ref,
                entry.Pack.Commit,
                approved = entry.ApprovedCommit,
                active = entry.IsActive,
                reason = entry.Reason.ToString().ToLowerInvariant(),
            }));

            return CommandOutput.Success();
        }

        if (standing.Value!.Count == 0)
        {
            output.WriteLine("[dim]No specialist packs are declared.[/]");

            return CommandOutput.Success();
        }

        foreach (var entry in standing.Value!)
        {
            output.WriteLine(
                $"{(entry.IsActive ? "[green]+[/]" : "[yellow]-[/]")} "
                + $"[bold]{Markup.Escape(entry.Pack.Name),-20}[/] "
                + $"{Markup.Escape(PackGate.Explain(entry))}");
        }

        return CommandOutput.Success();
    }
}

/// <summary>Declares a pack and pins it, without approving it.</summary>
[Description("Declare a specialist pack from a Git remote. Fetching is not approving.")]
[CommandMeta(CommandCategory.AgentConfiguration,
    Intent = "add specialist pack house standards remote", Mutates = true)]
public sealed class PackAddCommand : AsyncCommand<PackAddCommand.Settings>
{
    private readonly IPackService _packs;
    private readonly IAnsiConsole _console;

    public PackAddCommand(IPackService packs, IAnsiConsole console)
    {
        _packs = packs;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "<name>")]
        [Description("What to call the pack locally.")]
        public string Name { get; init; } = string.Empty;

        [CommandArgument(1, "<remote>")]
        [Description("The Git remote it comes from.")]
        public string Remote { get; init; } = string.Empty;

        [CommandOption("--ref <REF>")]
        [Description("Branch or tag to pin from. Defaults to main.")]
        public string Reference { get; init; } = "main";
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        if (settings.DryRun)
        {
            output.WriteLine(
                $"Would declare [bold]{Markup.Escape(settings.Name)}[/] from "
                + $"{Shown.Safely(settings.Remote)}. Nothing was fetched or written.");

            return CommandOutput.Success();
        }

        var added = await _packs
            .AddAsync(settings.Name, settings.Remote, settings.Reference, cancellationToken)
            .ConfigureAwait(false);

        if (added.Failed)
        {
            return output.Fail(added);
        }

        var pack = added.Value!;

        output.WriteLine(
            $"[green]+[/] Declared [bold]{Markup.Escape(pack.Name)}[/], pinned to "
            + $"{pack.Commit[..Math.Min(12, pack.Commit.Length)]}.");

        // The important line. Somebody who read only the success message would
        // otherwise assume the specialists are now in play.
        output.WriteLine(
            "[yellow]note[/] nothing from it is loaded yet. Its content becomes instructions "
            + "an agent follows, so read it and then run "
            + $"[bold]loadout pack approve {Markup.Escape(pack.Name)}[/].");

        if (_packs.DirectoryFor(pack.Name) is { Length: > 0 } directory)
        {
            output.WriteLine($"  [dim]{Markup.Escape(directory)}[/]");
        }

        return CommandOutput.Success();
    }
}

/// <summary>Records that somebody on this machine has read a pack's pinned commit.</summary>
[Description("Approve a pack's pinned commit on this machine, having read it.")]
[CommandMeta(CommandCategory.AgentConfiguration,
    Intent = "approve trust specialist pack activate", Mutates = true)]
public sealed class PackApproveCommand : AsyncCommand<PackApproveCommand.Settings>
{
    private readonly IPackService _packs;
    private readonly IAnsiConsole _console;

    public PackApproveCommand(IPackService packs, IAnsiConsole console)
    {
        _packs = packs;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "<name>")]
        [Description("The pack to approve.")]
        public string Name { get; init; } = string.Empty;
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        if (settings.DryRun)
        {
            output.WriteLine(
                $"Would approve [bold]{Markup.Escape(settings.Name)}[/] on this machine. "
                + "Nothing was written.");

            return CommandOutput.Success();
        }

        var approved = await _packs
            .ApproveAsync(settings.Name, Environment.UserName, cancellationToken)
            .ConfigureAwait(false);

        if (approved.Failed)
        {
            return output.Fail(approved);
        }

        output.WriteLine(
            $"[green]+[/] Approved [bold]{Markup.Escape(settings.Name)}[/] on this machine.");
        output.WriteLine(
            "[dim]This machine only. Approving is taking responsibility for what an agent "
            + "will be told, and nobody can do that on somebody else's behalf.[/]");

        return CommandOutput.Success();
    }
}

/// <summary>Moves a pack's pin, which costs it its approval.</summary>
[Description("Move a pack's pin to what its ref points at now. Costs its approval.")]
[CommandMeta(CommandCategory.AgentConfiguration,
    Intent = "update specialist pack refresh pin", Mutates = true)]
public sealed class PackUpdateCommand : AsyncCommand<PackApproveCommand.Settings>
{
    private readonly IPackService _packs;
    private readonly IAnsiConsole _console;

    public PackUpdateCommand(IPackService packs, IAnsiConsole console)
    {
        _packs = packs;
        _console = console;
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        PackApproveCommand.Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        if (settings.DryRun)
        {
            output.WriteLine(
                $"Would move [bold]{Markup.Escape(settings.Name)}[/]'s pin. Nothing was "
                + "fetched or written.");

            return CommandOutput.Success();
        }

        var updated = await _packs
            .UpdateAsync(settings.Name, cancellationToken).ConfigureAwait(false);

        if (updated.Failed)
        {
            return output.Fail(updated);
        }

        var pack = updated.Value!;

        output.WriteLine(
            $"[green]+[/] [bold]{Markup.Escape(pack.Name)}[/] is now pinned to "
            + $"{pack.Commit[..Math.Min(12, pack.Commit.Length)]}.");

        output.WriteLine(
            "[yellow]note[/] it has stopped loading. What was approved was the content at the "
            + "old commit, so read the change and approve again.");

        return CommandOutput.Success();
    }
}

/// <summary>Stops declaring a pack.</summary>
[Description("Stop declaring a pack, and forget this machine's approval of it.")]
[CommandMeta(CommandCategory.AgentConfiguration,
    Intent = "remove delete specialist pack", Mutates = true)]
public sealed class PackRemoveCommand : AsyncCommand<PackApproveCommand.Settings>
{
    private readonly IPackService _packs;
    private readonly IAnsiConsole _console;

    public PackRemoveCommand(IPackService packs, IAnsiConsole console)
    {
        _packs = packs;
        _console = console;
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        PackApproveCommand.Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        if (settings.DryRun)
        {
            output.WriteLine(
                $"Would stop declaring [bold]{Markup.Escape(settings.Name)}[/]. "
                + "Nothing was written.");

            return CommandOutput.Success();
        }

        var removed = await _packs
            .RemoveAsync(settings.Name, cancellationToken).ConfigureAwait(false);

        if (removed.Failed)
        {
            return output.Fail(removed);
        }

        output.WriteLine(
            $"[green]+[/] Stopped declaring {Markup.Escape(settings.Name)}, and forgot this "
            + "machine's approval of it.");

        return CommandOutput.Success();
    }
}
