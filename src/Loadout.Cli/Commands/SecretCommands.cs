using Loadout.Tui;
using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Models;
using Loadout.Platform.Abstractions;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>Shared settings for the secret commands.</summary>
public class SecretSettings : GlobalSettings
{
    [CommandArgument(0, "<reference>")]
    [Description("Secret reference, for example anthropic/default.")]
    public string Reference { get; init; } = string.Empty;
}

/// <summary>
/// Stores a secret in the platform credential store (spec sections 54 and 55).
/// <para>
/// The value is never accepted as a command-line argument. An argument would
/// land in shell history, in the process list, and in any shell trace — which
/// is precisely what spec section 55 forbids. It is read from a masked prompt,
/// or from stdin when the launcher is being scripted.
/// </para>
/// </summary>
[Description("Store a secret in the platform credential store.")]
[CommandMeta(CommandCategory.Safety, Intent = "store secret credential token password", Mutates = true)]
public sealed class SecretSetCommand : AsyncCommand<SecretSettings>
{
    private readonly ISecretProvider _secrets;
    private readonly IAnsiConsole _console;

    public SecretSetCommand(ISecretProvider secrets, IAnsiConsole console)
    {
        _secrets = secrets;
        _console = console;
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, SecretSettings settings, CancellationToken cancellationToken)
    {
        var output = new CommandOutput(_console, settings);

        string value;

        if (Console.IsInputRedirected)
        {
            // Piped input is the scripted path: loadout secret set x < file.
            var piped = await Console.In.ReadToEndAsync().ConfigureAwait(false);
            value = piped.TrimEnd('\r', '\n');
        }
        else if (settings.AllowsPrompting)
        {
            value = _console.Prompt(
                new TextPrompt<string>($"Value for [bold]{Markup.Escape(settings.Reference)}[/]:")
                    .PromptStyle("red")
                    .Secret());
        }
        else
        {
            return output.Fail(
                "No value supplied. Pipe the secret on stdin when running non-interactively.",
                ExitCode.InvalidArguments);
        }

        if (string.IsNullOrEmpty(value))
        {
            return output.Fail("An empty secret was supplied.", ExitCode.InvalidArguments);
        }

        // Storing reaches the platform credential store, which is outside anything this can undo.
        if (settings.DryRun)
        {
            // The value is never echoed, on this path least of all: a preview
            // that printed what it would store would put the secret on screen
            // and in the scrollback of anyone checking first.
            output.WriteLine(
                $"[bold]Would store[/] a secret under "
                + $"{Markup.Escape(settings.Reference)}. Nothing was changed.");
        
            return CommandOutput.Success();
        }
        var result = await _secrets.SetAsync(settings.Reference, value).ConfigureAwait(false);
        if (result.Failed)
        {
            return output.Fail(result);
        }

        output.WriteLine(
            $"[green]Stored[/] {Markup.Escape(settings.Reference)} "
            + $"[dim]in {Markup.Escape(_secrets.Name)}[/]");

        return CommandOutput.Success();
    }
}

/// <summary>
/// Confirms that a secret reference resolves, without revealing it
/// (spec section 55). The value is deliberately never printed.
/// </summary>
[Description("Check that a secret reference resolves. Never prints the value.")]
[CommandMeta(CommandCategory.Safety, Intent = "test secret reference resolves works")]
public sealed class SecretTestCommand : AsyncCommand<SecretSettings>
{
    private readonly ISecretProvider _secrets;
    private readonly IAnsiConsole _console;

    public SecretTestCommand(ISecretProvider secrets, IAnsiConsole console)
    {
        _secrets = secrets;
        _console = console;
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, SecretSettings settings, CancellationToken cancellationToken)
    {
        var output = new CommandOutput(_console, settings);

        var result = await _secrets.TestAsync(settings.Reference).ConfigureAwait(false);

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                reference = settings.Reference,
                provider = _secrets.Name,
                resolved = result.Succeeded,
            });

            return result.Succeeded ? CommandOutput.Success() : (int)result.ExitCode;
        }

        if (result.Failed)
        {
            return output.Fail(result);
        }

        output.WriteLine(
            $"[green]Resolved[/] {Markup.Escape(settings.Reference)} "
            + $"[dim]via {Markup.Escape(_secrets.Name)}[/]");

        return CommandOutput.Success();
    }
}

/// <summary>Deletes a secret from the platform credential store (spec section 55).</summary>
[Description("Remove a secret from the platform credential store.")]
[CommandMeta(CommandCategory.Safety, Intent = "remove delete secret credential", Mutates = true)]
public sealed class SecretRemoveCommand : AsyncCommand<SecretSettings>
{
    private readonly ISecretProvider _secrets;
    private readonly IAnsiConsole _console;

    public SecretRemoveCommand(ISecretProvider secrets, IAnsiConsole console)
    {
        _secrets = secrets;
        _console = console;
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, SecretSettings settings, CancellationToken cancellationToken)
    {
        var output = new CommandOutput(_console, settings);

        // Removing a credential cannot be undone from here, so the preview stops short of it.
        if (settings.DryRun)
        {
            output.WriteLine(
                $"[bold]Would remove[/] the secret at "
                + $"{Markup.Escape(settings.Reference)}. Nothing was changed.");
        
            return CommandOutput.Success();
        }
        var result = await _secrets.RemoveAsync(settings.Reference).ConfigureAwait(false);
        if (result.Failed)
        {
            return output.Fail(result);
        }

        output.WriteLine($"[green]Removed[/] {Markup.Escape(settings.Reference)}");

        return CommandOutput.Success();
    }
}
