using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Loadout.Core.Security;
using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Teams.Daemon;

/// <summary>One positional argument of a command, as its settings declare it.</summary>
public sealed record StudioArgument(string Name, int Position, bool Required, bool Many, string Description);

/// <summary>One option of a command, as its settings declare it.</summary>
/// <param name="Name">The long name, with its dashes: <c>--project</c>.</param>
/// <param name="Short">The short name with its dash, if it has one.</param>
/// <param name="Kind"><c>flag</c> takes no value, <c>value</c> one, <c>values</c> one or more, <c>optional</c> may take one.</param>
/// <param name="ValueName">What the value is called in the help, for a placeholder.</param>
/// <param name="Description">The help line.</param>
/// <param name="Required">Whether the parser refuses the command without it.</param>
/// <param name="Global">Whether every command has it, so a form can tuck it away.</param>
/// <param name="Choices">The values it accepts, when they are a fixed set.</param>
public sealed record StudioOption(
    string Name,
    string? Short,
    string Kind,
    string? ValueName,
    string Description,
    bool Required,
    bool Global,
    IReadOnlyList<string> Choices);

/// <summary>A command as Studio's forms see it: what it is, what it takes, and what running it would do.</summary>
public sealed record StudioCommand(
    string Path,
    string Description,
    string Category,
    string Example,
    bool Runnable,
    StudioReach Reach,
    IReadOnlyList<StudioArgument> Arguments,
    IReadOnlyList<StudioOption> Options);

/// <summary>
/// The commands Studio may run and the way to run one, supplied by whoever
/// starts the server.
/// </summary>
/// <remarks>
/// An interface here and its implementation beside the commands, because the
/// catalogue is built where the parser is configured and Core does not reach
/// it. Running one starts this same launcher again as a child, so a job is
/// exactly what typing the command would have done.
/// </remarks>
public interface IStudioCommands
{
    /// <summary>Every command, with its arguments and options.</summary>
    IReadOnlyList<StudioCommand> Catalogue { get; }

    /// <summary>Starts <c>loadout</c> with these words after it, its output piped back.</summary>
    Task<OperationResult<IPipedProcess>> StartAsync(IReadOnlyList<string> words, CancellationToken ct);
}

/// <summary>One command run from Studio, while it runs and after.</summary>
public sealed class CommandJob
{
    internal CommandJob(string id, string path, IReadOnlyList<string> words, DateTimeOffset startedAt)
    {
        Id = id;
        Path = path;
        Words = words;
        StartedAt = startedAt;
    }

    /// <summary>What the page names it by.</summary>
    public string Id { get; }

    /// <summary>The command it ran.</summary>
    public string Path { get; }

    /// <summary>Everything after <c>loadout</c>.</summary>
    public IReadOnlyList<string> Words { get; }

    /// <summary>When it started.</summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>When it ended, while it has not.</summary>
    public DateTimeOffset? EndedAt { get; internal set; }

    /// <summary><c>running</c>, <c>done</c>, <c>failed</c> or <c>cancelled</c>.</summary>
    public string State { get; internal set; } = "running";

    /// <summary>The exit code, once it has one.</summary>
    public int? ExitCode { get; internal set; }

    /// <summary>Whether its output was longer than is kept.</summary>
    public bool Truncated { get; internal set; }

    internal StringBuilder Output { get; } = new();

    internal StringBuilder Error { get; } = new();

    internal IPipedProcess? Process { get; set; }

    internal bool CancelAsked { get; set; }

    /// <summary>The line as somebody would type it, each word quoted where it has to be.</summary>
    public string Line => "loadout " + string.Join(' ', Words.Select(CommandsApi.Quoted));
}

/// <summary>
/// The commands Studio has run, kept while the daemon runs.
/// </summary>
/// <remarks>
/// <para>
/// Each runs as a child of this launcher, so nothing here implements what a
/// command does: the parser is handed the words and decides. The child's
/// output is kept for the page, capped, and passed through the secret
/// redactor line by line. It never goes to the daemon's log, which is told
/// only which command ran, because the words after it can carry a value
/// somebody would not want written down.
/// </para>
/// <para>
/// Kept in memory and lost with the daemon, like hosted sessions. Somebody
/// wanting a record of what was changed has the command's own: backups,
/// journals, and the workspace's history.
/// </para>
/// </remarks>
public sealed class CommandJobs
{
    /// <summary>How much of a job's standard output is kept.</summary>
    internal const int OutputLimit = 2 * 1024 * 1024;

    /// <summary>How much of its standard error.</summary>
    internal const int ErrorLimit = 128 * 1024;

    /// <summary>How many finished jobs are remembered.</summary>
    internal const int Kept = 100;

    /// <summary>How many may run at once.</summary>
    /// <remarks>
    /// Each is a whole launcher starting up, and Windows refuses to start a
    /// process when too many are asked for at once. Eight is more than a
    /// person clicks through; a page stuck in a loop is held to it.
    /// </remarks>
    internal const int Running = 8;

    private readonly IStudioCommands _commands;
    private readonly TimeProvider _time;
    private readonly Action<string>? _ran;
    private readonly ConcurrentDictionary<string, CommandJob> _jobs = new(StringComparer.Ordinal);
    private int _stamp;

    /// <summary>Creates the list.</summary>
    /// <param name="commands">The catalogue and the way to run a command.</param>
    /// <param name="time">The clock.</param>
    /// <param name="ran">
    /// Told the command each time one starts, for the terminal serving the
    /// page: a page that can change things should not do it unseen. Only the
    /// command's name, never the words after it.
    /// </param>
    public CommandJobs(IStudioCommands commands, TimeProvider time, Action<string>? ran = null)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(time);

        _commands = commands;
        _time = time;
        _ran = ran;
    }

    /// <summary>The catalogue the jobs are run from.</summary>
    public IStudioCommands Commands => _commands;

    /// <summary>Changes whenever a job starts or ends, for the event stream to notice.</summary>
    public int Stamp => Volatile.Read(ref _stamp);

    /// <summary>Every job remembered, newest first.</summary>
    public IReadOnlyList<CommandJob> All => [.. _jobs.Values.OrderByDescending(job => job.StartedAt).ThenByDescending(job => job.Id, StringComparer.Ordinal)];

    /// <summary>One job, if it is remembered.</summary>
    public CommandJob? Find(string id) => _jobs.TryGetValue(id, out var job) ? job : null;

    /// <summary>
    /// Checks what the page asked for and starts it.
    /// </summary>
    /// <param name="path">The command, as the catalogue names it.</param>
    /// <param name="words">Everything after the command's name, as the form built it.</param>
    /// <param name="watchOnly">Whether this server only watches, which allows only commands that read.</param>
    /// <param name="ct">Stops the start, not the job.</param>
    public async Task<OperationResult<CommandJob>> StartAsync(
        string path, IReadOnlyList<string> words, bool watchOnly, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(words);

        var command = _commands.Catalogue.FirstOrDefault(one => string.Equals(one.Path, path, StringComparison.Ordinal));

        if (command is null)
        {
            return OperationResult<CommandJob>.Fail($"There is no command called 'loadout {path}'.", ExitCode.InvalidArguments);
        }

        if (Refusal(command, words, watchOnly) is { } refused)
        {
            return OperationResult<CommandJob>.Fail(refused, ExitCode.PolicyViolation);
        }

        if (_jobs.Values.Count(job => job.State == "running") >= Running)
        {
            return OperationResult<CommandJob>.Fail(
                $"{Running} commands are already running from Studio. Wait for one to finish, or cancel one.",
                ExitCode.PolicyViolation);
        }

        var started = await LaunchAsync(path, words, ct).ConfigureAwait(false);

        if (started.Failed)
        {
            return started;
        }

        var job = started.Value!;

        _jobs[job.Id] = job;
        _ran?.Invoke(path);
        Forget();
        Interlocked.Increment(ref _stamp);

        _ = FollowAsync(job, announce: true);

        return OperationResult<CommandJob>.Ok(job);
    }

    /// <summary>
    /// Runs a command that only reads and waits for what it says, for a page
    /// to show, without keeping it as a job.
    /// </summary>
    /// <remarks>
    /// The pages read through the same commands somebody would type, so what
    /// they show is what the terminal shows. These are not jobs: a page reading
    /// its projects every time it opens would bury what somebody ran by hand,
    /// and the daemon's terminal is not told about them either. Anything that
    /// would change something is refused here whether or not the server only
    /// watches; that goes through <see cref="StartAsync"/>, where it is seen.
    /// </remarks>
    public async Task<OperationResult<CommandJob>> ReadAsync(
        string path, IReadOnlyList<string> words, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(words);

        var command = _commands.Catalogue.FirstOrDefault(one => string.Equals(one.Path, path, StringComparison.Ordinal));

        if (command is null)
        {
            return OperationResult<CommandJob>.Fail($"There is no command called 'loadout {path}'.", ExitCode.InvalidArguments);
        }

        if (Refusal(command, words, watchOnly: true) is { } refused)
        {
            return OperationResult<CommandJob>.Fail(
                command.Reach.Effect is StudioEffect.Changes && !command.Reach.Reads(words)
                    ? $"'loadout {path}' changes something, so a page cannot read with it. Run it as a job."
                    : refused,
                ExitCode.PolicyViolation);
        }

        if (!await _reading.WaitAsync(ReadFor, ct).ConfigureAwait(false))
        {
            return OperationResult<CommandJob>.Fail("Too many pages are reading at once. Try again in a moment.", ExitCode.PolicyViolation);
        }

        try
        {
            var started = await LaunchAsync(path, words, ct).ConfigureAwait(false);

            if (started.Failed)
            {
                return started;
            }

            var job = started.Value!;
            var following = FollowAsync(job, announce: false);

            // A read that has not finished in time is stopped rather than left
            // holding a slot: the page waiting on it has long since given up.
            if (await Task.WhenAny(following, Task.Delay(ReadFor, ct)).ConfigureAwait(false) != following)
            {
                job.CancelAsked = true;
                job.Process?.Kill();
                await following.ConfigureAwait(false);
            }

            return OperationResult<CommandJob>.Ok(job);
        }
        finally
        {
            _reading.Release();
        }
    }

    /// <summary>How long a page's read may take.</summary>
    /// <remarks>
    /// Two minutes because 'usage' reads every transcript in the period, which
    /// took twenty seconds for thirty days on a machine with months of them.
    /// </remarks>
    internal static readonly TimeSpan ReadFor = TimeSpan.FromMinutes(2);

    private readonly SemaphoreSlim _reading = new(Running, Running);

    /// <summary>Starts the child with --non-interactive and its input closed.</summary>
    private async Task<OperationResult<CommandJob>> LaunchAsync(string path, IReadOnlyList<string> words, CancellationToken ct)
    {
        // Never asked anything: a job has nobody at a prompt, and a question
        // it waits on would hold it open for good. Every command takes this.
        List<string> line = [.. path.Split(' '), .. words];

        if (!line.Contains("--non-interactive", StringComparer.Ordinal))
        {
            line.Add("--non-interactive");
        }

        var job = new CommandJob(Guid.NewGuid().ToString("N")[..12], path, line, _time.GetUtcNow());
        var started = await _commands.StartAsync(line, ct).ConfigureAwait(false);

        if (started.Failed)
        {
            return OperationResult<CommandJob>.Fail(started.Error!, started.ExitCode);
        }

        job.Process = started.Value!;

        // The child is never asked anything, so its input closes at once and a
        // prompt that slipped past --non-interactive reads the end of it.
        await job.Process.CloseInputAsync().ConfigureAwait(false);

        return OperationResult<CommandJob>.Ok(job);
    }

    /// <summary>Stops a running job and what it started.</summary>
    public bool Cancel(string id)
    {
        if (Find(id) is not { State: "running", Process: { } process } job)
        {
            return false;
        }

        job.CancelAsked = true;
        process.Kill();

        return true;
    }

    /// <summary>
    /// Why this cannot be run from the page, or null when it can.
    /// </summary>
    /// <remarks>
    /// The parser checks the words properly when the child starts. This only
    /// holds the line to what the form could have built: positional words
    /// that are not options, and options this command declares. Anything
    /// else is a page doing something its form does not offer.
    /// </remarks>
    internal static string? Refusal(StudioCommand command, IReadOnlyList<string> words, bool watchOnly)
    {
        switch (command.Reach.Effect)
        {
            case StudioEffect.TerminalOnly:
                return $"'loadout {command.Path}' {command.Reach.Reason}. Run it in a terminal.";

            case StudioEffect.Session:
                return $"'loadout {command.Path}' starts an agent session. Start it from Sessions, where Studio hosts it.";
        }

        if (!command.Runnable)
        {
            return $"'loadout {command.Path}' is a group of commands. Choose one of them.";
        }

        if (watchOnly && !command.Reach.Reads(words))
        {
            return $"This dashboard is only watching, so it cannot run 'loadout {command.Path}'. Run it in a terminal.";
        }

        var options = command.Options.ToDictionary(option => option.Name, StringComparer.Ordinal);

        foreach (var option in command.Options.Where(option => option.Short is not null))
        {
            options.TryAdd(option.Short!, option);
        }

        StudioCoverage.Refused.TryGetValue(command.Path, out var refused);

        for (var i = 0; i < words.Count; i++)
        {
            var word = words[i];

            if (word.Length == 0 || word.Contains('\0', StringComparison.Ordinal))
            {
                return "One of the words is empty.";
            }

            if (word[0] != '-')
            {
                continue;
            }

            var name = word.Split('=', 2)[0];

            if (!options.TryGetValue(name, out var option))
            {
                return $"'loadout {command.Path}' has no option '{name}'.";
            }

            if (refused is not null && refused.TryGetValue(option.Name, out var why))
            {
                return $"Studio does not pass {option.Name}: {why}. Run it in a terminal.";
            }

            // A value given as the next word is skipped, so a value is never
            // mistaken for a positional argument. The parser refuses a value
            // that starts with a dash anyway, so the form joins those with '='.
            if (option.Kind is "value" or "values" && !word.Contains('=', StringComparison.Ordinal))
            {
                i++;
            }
        }

        return null;
    }

    private async Task FollowAsync(CommandJob job, bool announce)
    {
        var process = job.Process!;

        try
        {
            await Task.WhenAll(
                ReadAsync(process.Output, job, job.Output, OutputLimit),
                ReadAsync(process.Error, job, job.Error, ErrorLimit)).ConfigureAwait(false);

            var code = await process.Exited.ConfigureAwait(false);

            job.ExitCode = code;
            job.State = job.CancelAsked ? "cancelled" : code == 0 ? "done" : "failed";
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            job.State = job.CancelAsked ? "cancelled" : "failed";
        }
        finally
        {
            job.EndedAt = _time.GetUtcNow();

            if (announce)
            {
                Interlocked.Increment(ref _stamp);
            }

            await process.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task ReadAsync(TextReader reader, CommandJob job, StringBuilder into, int limit)
    {
        while (await reader.ReadLineAsync(CancellationToken.None).ConfigureAwait(false) is { } line)
        {
            lock (into)
            {
                if (into.Length + line.Length + 1 > limit)
                {
                    // Read to the end regardless, or the child blocks on a full
                    // pipe and never exits.
                    job.Truncated = true;

                    continue;
                }

                into.Append(Checked(line)).Append('\n');
            }
        }
    }

    /// <summary>
    /// A line with any secret in it replaced, or withheld when it cannot be checked.
    /// </summary>
    /// <remarks>
    /// The redactor gives up on a long run of characters with no separators,
    /// and text it could not check is not served.
    /// </remarks>
    internal static string Checked(string line)
    {
        try
        {
            return SecretRedactor.Redact(line);
        }
        catch (RegexMatchTimeoutException)
        {
            return "[a line too long to check for secrets was left out]";
        }
    }

    private void Forget()
    {
        foreach (var old in _jobs.Values
            .Where(job => job.State != "running")
            .OrderByDescending(job => job.StartedAt)
            .Skip(Kept))
        {
            _jobs.TryRemove(old.Id, out _);
        }
    }
}

/// <summary>Studio's commands and jobs under <c>/api/v1/commands</c> and <c>/api/v1/jobs</c>.</summary>
/// <remarks>
/// <list type="table">
/// <item><term>GET /api/v1/commands</term><description>Every command, its arguments, options and reach.</description></item>
/// <item><term>GET /api/v1/jobs</term><description>The jobs remembered, newest first, without their output.</description></item>
/// <item><term>POST /api/v1/jobs</term><description><c>{"path", "words"}</c>: 202 with the job, or 400 or 403 with why not.</description></item>
/// <item><term>GET /api/v1/jobs/{id}</term><description>One job with its output.</description></item>
/// <item><term>DELETE /api/v1/jobs/{id}</term><description>Cancels it.</description></item>
/// <item><term>POST /api/v1/read</term><description><c>{"path", "words"}</c> for a command that only reads: 200 with what it said, kept nowhere.</description></item>
/// </list>
/// </remarks>
internal static class CommandsApi
{
    public const string CommandsRoot = "/api/v1/commands";

    public const string JobsRoot = "/api/v1/jobs";

    public const string ReadRoot = "/api/v1/read";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private sealed record Asked(string? Path, List<string>? Words);

    /// <summary>A word quoted as a shell would need it, for showing the line.</summary>
    internal static string Quoted(string word) =>
        word.Length > 0 && word.All(c => char.IsLetterOrDigit(c) || "-_./:=@,+%".Contains(c, StringComparison.Ordinal))
            ? word
            : "'" + word.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    public static async Task HandleAsync(HttpListenerContext context, CommandJobs jobs, string path, bool watchOnly, CancellationToken ct)
    {
        var method = context.Request.HttpMethod;

        if (path == CommandsRoot && method == "GET")
        {
            await JsonAsync(context, 200, jobs.Commands.Catalogue).ConfigureAwait(false);

            return;
        }

        if (path == ReadRoot && method == "POST")
        {
            if (await AskedAsync(context, ct).ConfigureAwait(false) is not { Path: { Length: > 0 } reading } asked)
            {
                await DashboardServer.WriteAsync(context, 400, "text/plain; charset=utf-8",
                    "The body should be JSON naming a 'path', with its 'words' after it.").ConfigureAwait(false);

                return;
            }

            var read = await jobs.ReadAsync(reading, asked.Words ?? [], ct).ConfigureAwait(false);

            if (read.Failed)
            {
                await DashboardServer.WriteAsync(context, read.ExitCode == ExitCode.PolicyViolation ? 403 : 400,
                    "text/plain; charset=utf-8", read.Error!).ConfigureAwait(false);

                return;
            }

            await JsonAsync(context, 200, Shown(read.Value!, withOutput: true)).ConfigureAwait(false);

            return;
        }

        if (path == JobsRoot && method == "GET")
        {
            await JsonAsync(context, 200, jobs.All.Select(job => Shown(job, withOutput: false))).ConfigureAwait(false);

            return;
        }

        if (path == JobsRoot && method == "POST")
        {
            var asked = await AskedAsync(context, ct).ConfigureAwait(false);

            if (asked?.Path is not { Length: > 0 } command)
            {
                await DashboardServer.WriteAsync(context, 400, "text/plain; charset=utf-8",
                    "The body should be JSON naming a 'path', with its 'words' after it.").ConfigureAwait(false);

                return;
            }

            var started = await jobs.StartAsync(command, asked.Words ?? [], watchOnly, ct).ConfigureAwait(false);

            if (started.Failed)
            {
                await DashboardServer.WriteAsync(context, started.ExitCode == ExitCode.PolicyViolation ? 403 : 400,
                    "text/plain; charset=utf-8", started.Error!).ConfigureAwait(false);

                return;
            }

            await JsonAsync(context, 202, Shown(started.Value!, withOutput: false)).ConfigureAwait(false);

            return;
        }

        if (path.StartsWith(JobsRoot + "/", StringComparison.Ordinal))
        {
            var id = path[(JobsRoot.Length + 1)..];

            if (jobs.Find(id) is not { } job)
            {
                await DashboardServer.WriteAsync(context, 404, "text/plain; charset=utf-8",
                    "There is no such job. Jobs are forgotten when the daemon stops.").ConfigureAwait(false);

                return;
            }

            if (method == "GET")
            {
                await JsonAsync(context, 200, Shown(job, withOutput: true)).ConfigureAwait(false);

                return;
            }

            if (method == "DELETE")
            {
                if (watchOnly)
                {
                    await DashboardServer.WriteAsync(context, 403, "text/plain; charset=utf-8",
                        "This dashboard is only watching, so it cannot cancel anything.").ConfigureAwait(false);

                    return;
                }

                jobs.Cancel(id);
                context.Response.StatusCode = 204;
                context.Response.Close();

                return;
            }
        }

        await DashboardServer.WriteAsync(context, 405, "text/plain; charset=utf-8",
            "Commands are read with GET; jobs are started with POST, read with GET and cancelled with DELETE.").ConfigureAwait(false);
    }

    private static async Task<Asked?> AskedAsync(HttpListenerContext context, CancellationToken ct)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<Asked>(context.Request.InputStream, Json, ct).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static object Shown(CommandJob job, bool withOutput)
    {
        string? output = null, error = null;

        if (withOutput)
        {
            lock (job.Output)
            {
                output = job.Output.ToString();
            }

            lock (job.Error)
            {
                error = job.Error.ToString();
            }
        }

        return new
        {
            id = job.Id,
            path = job.Path,
            line = job.Line,
            state = job.State,
            exitCode = job.ExitCode,
            startedAt = job.StartedAt,
            endedAt = job.EndedAt,
            truncated = job.Truncated,
            output,
            error,
        };
    }

    private static Task JsonAsync(HttpListenerContext context, int status, object body) =>
        DashboardServer.WriteAsync(context, status, "application/json; charset=utf-8", JsonSerializer.Serialize(body, Json));
}
