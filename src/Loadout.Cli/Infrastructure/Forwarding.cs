using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Loadout.Core.Teams.Daemon;
using Loadout.Platform.Abstractions;
using Spectre.Console;

namespace Loadout.Cli.Infrastructure;

/// <summary>
/// Hands a command typed in a terminal to a running daemon of the same version,
/// which runs it and sends back what it says.
/// </summary>
/// <remarks>
/// <para>
/// Most of a short command's time is the launcher starting: the runtime, every
/// service, the parser. A daemon has all of that running already. So when one
/// of this exact version is up, a command that cannot need the terminal is run
/// there, drawn for this terminal's width and colours, and its output and exit
/// code come back as if it had run here.
/// </para>
/// <para>
/// Anything that might need this terminal runs here, as it always did: a
/// command that asks questions, reads what is piped into it, puts a screen or
/// an agent in this window, opens an editor, or is the daemon itself. A command
/// that changes something is forwarded only when it was told not to ask
/// anything. And whatever goes wrong reaching the daemon - none running, a
/// different version, a refusal - the command runs here instead.
/// </para>
/// </remarks>
internal static class Forwarding
{
    /// <summary>Set to anything to run every command here, whatever is running.</summary>
    internal const string Variable = "LOADOUT_LOCAL";

    /// <summary>How long reaching a daemon may take before the command runs here instead.</summary>
    private static readonly TimeSpan Reaching = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Commands that stay in the terminal they were typed in whatever was
    /// passed, beyond those Studio already knows need one.
    /// </summary>
    private static readonly HashSet<string> Local = new(StringComparer.Ordinal)
    {
        // Open an editor or a window from the environment of the terminal.
        "code", "project open", "workspace open", "config edit",

        // Work attached to this terminal for as long as it takes, which must
        // not go with the daemon when it restarts.
        "team run", "team resume",

        // Replace or control the daemon itself.
        "update", "daemon", "daemon stop", "daemon restart", "daemon pause", "daemon resume", "daemon continue",
        "team daemon", "team daemon stop", "team daemon restart", "team daemon pause", "team daemon resume",
        "team daemon continue", "dashboard", "team dashboard",
    };

    /// <summary>
    /// Commands that read standard input, which stay here while it is a pipe
    /// or a file: a daemon running one would hand it nothing to read.
    /// </summary>
    /// <remarks>
    /// Every other command is forwarded whatever standard input is, because a
    /// script or a CI job always has it redirected and reads nothing from it.
    /// The architecture tests hold this to every command that reads it.
    /// </remarks>
    internal static readonly IReadOnlySet<string> ReadsInput = new HashSet<string>(StringComparer.Ordinal)
    {
        "dashboard signal", "docs refresh", "hook session-start", "idea apply", "idea dump add", "secret set",
        "statusline", "task apply", "trigger token set", "voice check",
    };

    /// <summary>
    /// Whether a command may be run by a daemon rather than here.
    /// </summary>
    /// <param name="arguments">Everything after <c>loadout</c>, rewritten as the parser sees it.</param>
    /// <param name="inputRedirected">Whether standard input is a pipe or a file, which some commands read.</param>
    /// <param name="local">The variable asking for everything to run here.</param>
    internal static bool Forwardable(IReadOnlyList<string> arguments, bool inputRedirected, string? local)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0 || !string.IsNullOrEmpty(local))
        {
            return false;
        }

        // A profile asked for on the command line or by the environment is
        // this terminal's; the daemon would settle its own.
        if (arguments.Any(word => word.StartsWith(AccessibleMode.Flag, StringComparison.Ordinal))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(AccessibleMode.Variable)))
        {
            return false;
        }

        if (PathOf(arguments) is not { } path || Local.Contains(path) || (inputRedirected && ReadsInput.Contains(path)))
        {
            return false;
        }

        if (!StudioCoverage.Reach.TryGetValue(path, out var reach))
        {
            return false;
        }

        return reach.Effect switch
        {
            StudioEffect.Reads => true,
            StudioEffect.Changes => reach.Reads(arguments)
                || arguments.Contains("--json", StringComparer.Ordinal)
                || arguments.Contains("--non-interactive", StringComparer.Ordinal),
            _ => false,
        };
    }

    /// <summary>The longest run of leading words that names a command.</summary>
    private static string? PathOf(IReadOnlyList<string> arguments)
    {
        var words = arguments.TakeWhile(word => word.Length > 0 && word[0] != '-').Take(3).ToList();

        for (var take = words.Count; take > 0; take--)
        {
            var path = string.Join(' ', words.Take(take));

            if (Catalogue.TypeOf(path) is not null)
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>
    /// Runs the command in a daemon and returns its exit code, or null when it
    /// should run here.
    /// </summary>
    /// <param name="arguments">Everything after <c>loadout</c>, rewritten as the parser sees it.</param>
    /// <param name="paths">Where the daemon leaves its note.</param>
    /// <param name="processes">Whether the process the note names is still the daemon.</param>
    /// <param name="ct">Stops the command, which the daemon then ends.</param>
    internal static async Task<int?> TryAsync(
        string[] arguments, IPlatformPaths paths, IProcessInspector processes, CancellationToken ct)
    {
        if (!Forwardable(arguments, Loadout.Platform.Common.Invocation.IsInputRedirected, Environment.GetEnvironmentVariable(Variable))
            || DaemonNote.Live(paths, processes) is not { Address: { Length: > 0 } address }
            || !Uri.TryCreate(address, UriKind.Absolute, out var where)
            || System.Web.HttpUtility.ParseQueryString(where.Query)["token"] is not { Length: > 0 } token)
        {
            return null;
        }

        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{where.Port}"), Timeout = Timeout.InfiniteTimeSpan };

        client.DefaultRequestHeaders.Add("X-Loadout-Token", token);

        var shape = AnsiConsole.Console.Profile;

        using var request = new HttpRequestMessage(HttpMethod.Post, RunApi.Root)
        {
            Content = JsonContent.Create(new
            {
                version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
                words = arguments,
                directory = Loadout.Platform.Common.Invocation.CurrentDirectory,
                width = shape.Width,
                ansi = shape.Capabilities.Ansi && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR")),
                colours = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR")) ? Bits(shape.Capabilities.ColorSystem) : 0,
            }),
        };

        HttpResponseMessage response;

        try
        {
            using var reaching = CancellationTokenSource.CreateLinkedTokenSource(ct);

            reaching.CancelAfter(Reaching);

            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, reaching.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            return null;
        }

        using (response)
        {
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var reader = new StreamReader(stream);

            // Through the process's own writers, in whatever encoding this
            // terminal uses, as a command run here would have written it.
            var output = Console.Out;
            var error = Console.Error;

            while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { Length: > 0 } line)
            {
                using var frame = JsonDocument.Parse(line);
                var root = frame.RootElement;

                if (root.TryGetProperty("exit", out var exit))
                {
                    return exit.GetInt32();
                }

                if (root.TryGetProperty("out", out var said))
                {
                    await WriteAsync(output, said.GetString(), ct).ConfigureAwait(false);
                }
                else if (root.TryGetProperty("err", out var complained))
                {
                    await WriteAsync(error, complained.GetString(), ct).ConfigureAwait(false);
                }
            }

            // The daemon went before saying how it ended.
            await WriteAsync(error, "The daemon running this command stopped before it finished.\n", ct).ConfigureAwait(false);

            return (int)Loadout.Models.ExitCode.GeneralFailure;
        }
    }

    private static async Task WriteAsync(TextWriter writer, string? text, CancellationToken ct)
    {
        if (text is null)
        {
            return;
        }

        await writer.WriteAsync(
            (Environment.NewLine == "\n" ? text : text.Replace("\n", Environment.NewLine, StringComparison.Ordinal)).AsMemory(),
            ct).ConfigureAwait(false);
        await writer.FlushAsync(ct).ConfigureAwait(false);
    }

    private static int Bits(ColorSystem colours) => colours switch
    {
        ColorSystem.TrueColor => 24,
        ColorSystem.EightBit => 8,
        ColorSystem.Standard => 4,
        ColorSystem.Legacy => 3,
        _ => 0,
    };
}
