using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Models;
using Loadout.Platform.Abstractions;
using Spectre.Console;
using Spectre.Console.Cli;
using Loadout.Tui;

namespace Loadout.Cli.Commands;

/// <summary>
/// Emits a shell completion script (spec section 41).
/// <para>
/// The scripts complete project names by calling back into
/// <c>loadout project list --json</c>, so completions stay correct as projects
/// are added and removed without the script being regenerated. That is also
/// why stable JSON output matters: the completion scripts are the launcher's
/// own first consumer of it.
/// </para>
/// </summary>
[Description("Print a shell completion script for powershell, bash, zsh or fish.")]
[CommandMeta(CommandCategory.Integration, Intent = "shell tab complete bash zsh fish", Example = "zsh")]
public sealed class CompletionCommand : Command<CompletionCommand.Settings>
{
    /// <summary>
    /// Every top-level command, taken from the set the parser fills as it
    /// registers them.
    /// </summary>
    /// <remarks>
    /// This was a hand-written array of nine, and stayed nine while the command
    /// line grew to about forty: <c>memory</c>, <c>instructions</c>,
    /// <c>usage</c>, <c>sessions</c>, <c>task</c>, <c>mcp</c>, <c>config</c> and
    /// the rest never completed, and nothing said so. It is the same second
    /// list the launcher's catalogue exists to avoid — the parity tests were
    /// written for exactly this failure on the screens, and the completions
    /// were a third copy nobody had looked at.
    /// </remarks>
    internal static IEnumerable<string> TopLevelCommands =>
        Program.CommandNames().Order(StringComparer.Ordinal);

    private readonly IShellProvider _shells;
    private readonly IAnsiConsole _console;

    public CompletionCommand(IShellProvider shells, IAnsiConsole console)
    {
        _shells = shells;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "[shell]")]
        [Description("powershell, bash, zsh or fish. Detected from the environment when omitted.")]
        public string? Shell { get; init; }

        [CommandOption("--install-path")]
        [Description("Print where the script should be installed instead of the script itself.")]
        public bool ShowInstallPath { get; init; }
    }

    /// <inheritdoc />
    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var output = new CommandOutput(_console, settings);

        var shell = ParseShell(settings.Shell) ?? _shells.DetectCurrentShell();

        if (shell is null)
        {
            // Guessing here would hand the user a script their shell cannot
            // read, which is worse than asking.
            return output.Fail(
                "The shell could not be determined. Name one: powershell, bash, zsh or fish.",
                ExitCode.InvalidArguments);
        }

        if (settings.ShowInstallPath)
        {
            var pathResult = _shells.GetCompletionInstallPath(shell.Value);

            if (pathResult.Failed)
            {
                return output.Fail(pathResult);
            }

            Console.Out.WriteLine(pathResult.Value);
            return CommandOutput.Success();
        }

        // Written to the raw stream: the script is data to be redirected into a
        // file, so no markup or wrapping may touch it.
        Console.Out.WriteLine(Render(shell.Value));

        return CommandOutput.Success();
    }

    private static ShellKind? ParseShell(string? value) => value?.ToLowerInvariant() switch
    {
        "powershell" or "pwsh" => ShellKind.PowerShell,
        "bash" => ShellKind.Bash,
        "zsh" => ShellKind.Zsh,
        "fish" => ShellKind.Fish,
        _ => null,
    };

    /// <summary>
    /// The script for a shell named as the command line names it, so a test can
    /// read what a user would actually be given.
    /// </summary>
    internal static string ScriptFor(string shell) =>
        ParseShell(shell) is { } kind
            ? Render(kind)
            : throw new ArgumentException($"'{shell}' is not a shell.", nameof(shell));

    private static string Render(ShellKind shell)
    {
        var commands = string.Join(" ", TopLevelCommands);

        return shell switch
        {
            ShellKind.Bash => $$"""
                # loadout completion for bash
                _loadout_completions() {
                  local current="${COMP_WORDS[COMP_CWORD]}"
                  local commands="{{commands}}"
                  if [ "$COMP_CWORD" -eq 1 ]; then
                    local projects
                    projects=$(loadout project list --json 2>/dev/null \
                      | grep -o '"id": *"[^"]*"' | sed 's/.*: *"//; s/"//')
                    COMPREPLY=( $(compgen -W "$commands $projects" -- "$current") )
                  else
                    COMPREPLY=( $(compgen -W "$commands" -- "$current") )
                  fi
                }
                complete -F _loadout_completions loadout
                """,

            ShellKind.Zsh => $$"""
                #compdef loadout
                # loadout completion for zsh
                _loadout() {
                  local -a commands projects
                  commands=({{commands}})
                  if (( CURRENT == 2 )); then
                    projects=(${(f)"$(loadout project list --json 2>/dev/null \
                      | grep -o '"id": *"[^"]*"' | sed 's/.*: *"//; s/"//')"})
                    compadd -- $commands $projects
                  else
                    compadd -- $commands
                  fi
                }
                compdef _loadout loadout
                """,

            ShellKind.Fish => $$"""
                # loadout completion for fish
                function __loadout_projects
                  loadout project list --json 2>/dev/null \
                    | string match -r '"id": *"[^"]*"' \
                    | string replace -r '.*: *"' '' \
                    | string replace '"' ''
                end
                complete -c loadout -f
                complete -c loadout -n __fish_use_subcommand -a "{{commands}}"
                complete -c loadout -n __fish_use_subcommand -a "(__loadout_projects)"
                complete -c loadout -l json -d 'Machine-readable output'
                complete -c loadout -l offline -d 'Do not contact the network'
                complete -c loadout -l agent -d 'Agent to launch' -r
                """,

            _ => $$"""
                # loadout completion for PowerShell
                Register-ArgumentCompleter -Native -CommandName loadout -ScriptBlock {
                    param($wordToComplete, $commandAst, $cursorPosition)

                    $commands = @({{string.Join(", ", TopLevelCommands.Select(c => "'" + c + "'"))}})

                    $projects = @()
                    try {
                        $json = loadout project list --json 2>$null | ConvertFrom-Json
                        $projects = $json.projects | ForEach-Object { $_.id }
                    }
                    catch {
                        # No workspace yet, or loadout is mid-install. Complete
                        # the built-in commands rather than failing the prompt.
                    }

                    @($commands + $projects) |
                        Where-Object { $_ -like "$wordToComplete*" } |
                        ForEach-Object {
                            [System.Management.Automation.CompletionResult]::new(
                                $_, $_, 'ParameterValue', $_)
                        }
                }
                """,
        };
    }
}
