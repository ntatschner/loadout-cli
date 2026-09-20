using Loadout.Cli.Infrastructure;
using Loadout.Tui;
using Loadout.Core.Teams.Daemon;
using Loadout.Models;
using Loadout.Models.Results;
using Spectre.Console;

namespace Loadout.Cli.Commands;

/// <summary>
/// What a button on the dashboard does.
/// </summary>
/// <remarks>
/// <para>
/// Every one of them types the command a person would have typed and lets the
/// parser decide whether it means anything. Nothing here implements the
/// behaviour of answering a gate, holding a run or messaging a lead — there is
/// one implementation of each and it is the command, or there are two and one
/// of them drifts.
/// </para>
/// <para>
/// In its own class because two commands serve the page. The daemon has always
/// been able to act; <c>team dashboard</c> could not, so the page it serves
/// drew "Hold it", "Stop it" and a box for messaging the lead and the server
/// answered every one of them with "this server only reads". A page that offers
/// a control it cannot honour is worse than one that does not offer it.
/// </para>
/// </remarks>
internal static class DashboardActions
{
    /// <summary>Does what a button asked, by running the command it stands for.</summary>
    internal static async Task<OperationResult> RanAsync(
        ICommandCatalogue commands,
        TimeProvider time,
        RunAction action,
        CommandOutput output,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(output);

        var (command, arguments) = action.Verb switch
        {
            "gates" or "gate" => ("team gate", Gate(action)),
            "message" => ("team message", new List<string> { action.Run, "--message", action.Message ?? string.Empty }),
            "stop" => ("team halt", [action.Run]),
            "pause" => ("team halt", [action.Run, "--pause"]),
            "resume" => ("team halt", [action.Run, "--resume"]),

            // An empty name clears it, which is how the page offers "put it
            // back": there is one box, and emptying a box is what people do.
            "name" => ("team name", action.Room is { Length: > 0 } room
                ? [action.Run, "--room", room]
                : [action.Run, "--clear"]),

            "pr" => ("team pr", action.Node is { Length: > 0 } whose
                ? [action.Run, "--node", whose]
                : [action.Run]),

            "say" => ("team say", [
                action.Run,
                "--node", action.Node ?? string.Empty,
                "--message", action.Message ?? string.Empty,
            ]),
            _ => (string.Empty, []),
        };

        if (command.Length == 0)
        {
            return OperationResult.Fail(
                $"There is nothing called '{action.Verb}' to do to a run.", ExitCode.InvalidArguments);
        }

        if (action.Verb == "message" && action.Message is not { Length: > 0 })
        {
            return OperationResult.Fail("Say something to say.", ExitCode.InvalidArguments);
        }

        // Said where whoever started the server can see it. A page that can
        // stop a run should not be able to stop one silently.
        output.WriteLine(
            $"[dim]{time.GetUtcNow().ToLocalTime():HH:mm}[/] from the dashboard: "
            + $"{Markup.Escape(command)} {Markup.Escape(action.Run)}");

        var code = await commands
            .RunAsync(command, [.. arguments, "--non-interactive"], ct)
            .ConfigureAwait(false);

        return code == (int)ExitCode.Success
            ? OperationResult.Ok()
            : OperationResult.Fail($"'{command}' ended with exit code {code}.", ExitCode.GeneralFailure);
    }

    /// <summary>The command line for answering one gate.</summary>
    private static List<string> Gate(RunAction action)
    {
        var arguments = new List<string> { action.Run };

        if (action.Gate is { Length: > 0 } gate)
        {
            arguments.Add("--gate");
            arguments.Add(gate);
        }

        arguments.Add("--answer");
        arguments.Add(action.Answer ?? "no");
        arguments.Add("--by");
        arguments.Add("dashboard");

        if (action.Instead is { Length: > 0 } instead)
        {
            arguments.Add("--instead");
            arguments.Add(instead);
        }

        if (action.Reason is { Length: > 0 } reason)
        {
            arguments.Add("--reason");
            arguments.Add(reason);
        }

        return arguments;
    }
}
