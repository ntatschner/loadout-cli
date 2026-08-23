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
    public override async Task<int> ExecuteAsync(CommandContext context, SecretSettings settings)
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
    public override async Task<int> ExecuteAsync(CommandContext context, SecretSettings settings)
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
    public override async Task<int> ExecuteAsync(CommandContext context, SecretSettings settings)
    {
        var output = new CommandOutput(_console, settings);

        var result = await _secrets.RemoveAsync(settings.Reference).ConfigureAwait(false);
        if (result.Failed)
        {
            return output.Fail(result);
        }

        output.WriteLine($"[green]Removed[/] {Markup.Escape(settings.Reference)}");

        return CommandOutput.Success();
    }
}
