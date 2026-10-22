using System.Collections.Concurrent;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Ideas;
using Loadout.Core.Projects;
using Loadout.Core.Teams.Daemon;
using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Tui;
using Spectre.Console;

namespace Loadout.Cli.Commands;

/// <summary>
/// The ideas page of the dashboard: what it reads, and the command each of its
/// buttons types.
/// </summary>
/// <remarks>
/// <para>
/// One per server, because it remembers which rounds are going. A round with
/// an agent takes a minute or two and an HTTP request should not, so a refine
/// or a dump is started and answered at once, and the page learns it has
/// finished by reading again: an idea stays in <c>busy</c> until its command
/// has returned.
/// </para>
/// <para>
/// Remembered here and not on disk, and so only for this server. A round
/// started from a terminal is not shown as going, and a round started here is
/// forgotten if the server stops; either way the record says where the idea
/// got to, which is the part that matters.
/// </para>
/// </remarks>
internal sealed class DashboardIdeas
{
    private readonly ICommandCatalogue _commands;
    private readonly IIdeaService _ideas;
    private readonly IIdeaDumps _dumps;
    private readonly IProjectService _projects;
    private readonly TimeProvider _time;
    private readonly CommandOutput _output;
    private readonly ConcurrentDictionary<string, bool> _busy = new(StringComparer.Ordinal);
    private int _marked;

    /// <summary>A count that moves whenever a round from the page starts or finishes, for the event stream.</summary>
    public int Marked => Volatile.Read(ref _marked);

    public DashboardIdeas(
        ICommandCatalogue commands,
        IIdeaService ideas,
        IIdeaDumps dumps,
        IProjectService projects,
        TimeProvider time,
        CommandOutput output)
    {
        _commands = commands;
        _ideas = ideas;
        _dumps = dumps;
        _projects = projects;
        _time = time;
        _output = output;
    }

    /// <summary>Every verb the page can send, for the tests that run each one.</summary>
    internal static IReadOnlyList<string> Verbs =>
        ["add", "addto", "note-remove", "refine", "answer", "choose", "keep", "drop", "improve", "accept", "move", "remove", "dump", "apply"];

    /// <summary>How long an add with a refine is watched for its note before the page is answered anyway.</summary>
    internal static readonly TimeSpan AddPatience = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan AddPoll = TimeSpan.FromMilliseconds(100);

    /// <summary>The key a dump being split is kept busy under, until its id is known.</summary>
    internal const string Dumping = "(dump)";

    /// <summary>What the page draws: every idea in full, every dump, and what is going.</summary>
    public async Task<object> ReadAsync(CancellationToken ct)
    {
        var ideas = new List<object>();
        var listed = await _ideas.ListAsync(ct).ConfigureAwait(false);

        foreach (var idea in listed.Succeeded ? listed.Value! : [])
        {
            var read = await _ideas.ReadAsync(idea.Place, ct).ConfigureAwait(false);

            if (read.Succeeded)
            {
                ideas.Add(new
                {
                    title = idea.Title,
                    detail = IdeaView.Of(idea.Place, read.Value!),
                    moved = TaskTrace.Json(idea.Moved),
                });
            }
        }

        var dumps = await _dumps.ListAsync(ct).ConfigureAwait(false);
        var projects = await _projects.ListAsync(ct).ConfigureAwait(false);
        var movedOn = await _ideas.MovedOnAsync(ct).ConfigureAwait(false);

        return new
        {
            ideas,
            movedOn = movedOn.Succeeded
                ? movedOn.Value!
                    .OrderByDescending(one => one.Utc)
                    .Select(one => new { id = one.Id, title = one.Title, from = one.From, to = one.Where, at = one.Utc })
                    .ToList<object>()
                : [],
            dumps = dumps.Succeeded
                ? dumps.Value!
                    .Where(d => d.Dump.Items.Count == 0 || d.Dump.Items.Any(i => i.Recorded.Length == 0))
                    .Select(d => DumpView.Of(d.Place, d.Dump))
                    .ToList<object>()
                : [],
            busy = _busy.Keys.Where(key => key != Dumping).Order(StringComparer.Ordinal).ToList(),
            dumping = _busy.ContainsKey(Dumping),
            projects = projects.Succeeded ? projects.Value!.Select(p => p.Entry.Slug).ToList() : [],
        };
    }

    /// <summary>Does what the page asked, by typing the command for it.</summary>
    public async Task<OperationResult> DoAsync(IdeaAction asking, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(asking);

        var (command, arguments) = Maps(asking);

        if (command.Length == 0)
        {
            return OperationResult.Fail(
                $"There is nothing called '{asking.Verb}' to do to an idea.", ExitCode.InvalidArguments);
        }

        _output.WriteLine(
            $"[dim]{_time.GetUtcNow().ToLocalTime():HH:mm}[/] from the dashboard: "
            + $"{Markup.Escape(command)} {Markup.Escape(asking.Id ?? string.Empty)}");

        if (asking.Verb == "addto")
        {
            return await AddToAsync(command, arguments, asking, ct).ConfigureAwait(false);
        }

        var key = asking.Verb switch
        {
            "refine" => asking.Id,
            "dump" => Dumping,
            _ => null,
        };

        if (key is null)
        {
            return Said(asking.Verb, command, await _commands.RunAsync(command, arguments, ct).ConfigureAwait(false));
        }

        // One round at a time for an idea. Two at once would each read the
        // record, ask an agent, and write over the other's answer.
        if (!_busy.TryAdd(key, true))
        {
            return OperationResult.Fail(
                key == Dumping
                    ? "Some notes are being split already. Wait for that to finish."
                    : $"{asking.Id} has a round going already.",
                ExitCode.InvalidArguments);
        }

        Interlocked.Increment(ref _marked);

        // Not tied to the request's token: the request ends when this returns,
        // and the round has to outlive it. The server stopping ends the process.
        _ = Task.Run(async () =>
        {
            try
            {
                var code = await _commands.RunAsync(command, arguments, CancellationToken.None).ConfigureAwait(false);

                _output.WriteLine(
                    $"[dim]{_time.GetUtcNow().ToLocalTime():HH:mm}[/] {Markup.Escape(command)} "
                    + $"{Markup.Escape(asking.Id ?? string.Empty)} finished"
                    + (code == 0 ? "." : $" with exit code {code}."));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Said where somebody is watching rather than lost with the
                // task. The page sees the round end either way; the record
                // says what the agent made of it.
                _output.WriteLine($"[yellow]![/] {Markup.Escape(command)} stopped: {Markup.Escape(ex.Message)}");
            }
            finally
            {
                _busy.TryRemove(key, out _);
                Interlocked.Increment(ref _marked);
            }
        }, CancellationToken.None);

        return OperationResult.Ok();
    }

    /// <summary>
    /// What a finished command's exit code means for the page.
    /// </summary>
    /// <remarks>
    /// Guessed from the code, because the code is all that comes back: the
    /// command's own words go to the terminal serving the page, and the page
    /// says so rather than inventing a reason it cannot know.
    /// </remarks>
    private static OperationResult Said(string verb, string command, int code) =>
        code == (int)ExitCode.Success
            ? OperationResult.Ok()
            : OperationResult.Fail(
                ((ExitCode)code, verb) switch
                {
                    (ExitCode.PolicyViolation, _) =>
                        "That looks like it contains a credential, so nothing was recorded. Say it without the value.",
                    (ExitCode.ProjectNotFound, _) =>
                        "That project is not one this workspace knows about.",
                    (ExitCode.InvalidArguments, "addto") =>
                        "Nothing was added. The idea may have been accepted or changed since the page last read "
                        + "it: the page has read it again. The terminal serving this page has the exact reason.",
                    (ExitCode.InvalidArguments, "note-remove") =>
                        "The note was not removed. A round may have read it, the idea may have been accepted, or "
                        + "a round on it may be running. The terminal serving this page has the exact reason.",
                    (ExitCode.InvalidArguments, _) =>
                        "The idea could not take that. It may have changed since the page last read it: the "
                        + "page has read it again. The terminal serving this page has the exact reason.",
                    _ => $"'{command}' ended with exit code {code}. The terminal serving this page has the reason.",
                },
                (ExitCode)code);

    /// <summary>
    /// Adds a note to an idea, and with <see cref="IdeaAction.Refine"/> starts
    /// a round on it in the same command.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Without a refine it is quick, and awaited like any other step. With one
    /// it runs beside the request, as a refine does, kept busy under the idea.
    /// </para>
    /// <para>
    /// The page is answered once the note is in the record or the command has
    /// ended, whichever comes first, and told which happened by reading the
    /// record rather than guessing from the exit code: a failed round exits
    /// with the round's code, which can be the same code a refused add gives,
    /// and a failed refine read as a failed add gets the same note added
    /// twice.
    /// </para>
    /// </remarks>
    private async Task<OperationResult> AddToAsync(
        string command,
        IReadOnlyList<string> arguments,
        IdeaAction asking,
        CancellationToken ct)
    {
        if (!asking.Refine)
        {
            return Said(asking.Verb, command, await _commands.RunAsync(command, arguments, ct).ConfigureAwait(false));
        }

        var place = new IdeaPlace(asking.Project is { Length: > 0 } project ? project : null, asking.Id ?? string.Empty);
        var before = await NumberedAsync(place, ct).ConfigureAwait(false);

        if (!_busy.TryAdd(place.Id, true))
        {
            return OperationResult.Fail($"{place.Id} has a round going already.", ExitCode.InvalidArguments);
        }

        Interlocked.Increment(ref _marked);

        // Not tied to the request's token, for the same reason a refine is not.
        var run = Task.Run(async () =>
        {
            try
            {
                var code = await _commands.RunAsync(command, arguments, CancellationToken.None).ConfigureAwait(false);

                _output.WriteLine(
                    $"[dim]{_time.GetUtcNow().ToLocalTime():HH:mm}[/] {Markup.Escape(command)} --to "
                    + $"{Markup.Escape(place.Id)} finished" + (code == 0 ? "." : $" with exit code {code}."));

                return code;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _output.WriteLine($"[yellow]![/] {Markup.Escape(command)} stopped: {Markup.Escape(ex.Message)}");

                return (int)ExitCode.GeneralFailure;
            }
            finally
            {
                _busy.TryRemove(place.Id, out _);
                Interlocked.Increment(ref _marked);
            }
        }, CancellationToken.None);

        var until = _time.GetUtcNow() + AddPatience;

        while (true)
        {
            // Whether it had ended is taken before the record is read, so a
            // command that ended is judged on the record it left.
            var ended = run.IsCompleted;

            if (await NumberedAsync(place, ct).ConfigureAwait(false) > before)
            {
                return !ended || run.Result == (int)ExitCode.Success
                    ? OperationResult.Ok()
                    : OperationResult.Fail(
                        $"The note was added to {place.Id}, but the round on it failed with exit code {run.Result}, "
                        + "so do not add it again: refine it to try the round again. The terminal serving this "
                        + "page has the reason.",
                        (ExitCode)run.Result);
            }

            if (ended)
            {
                return Said(asking.Verb, command, run.Result);
            }

            if (_time.GetUtcNow() >= until)
            {
                return OperationResult.Fail(
                    $"The note had not reached {place.Id} after {AddPatience.TotalSeconds:0} seconds. The command "
                    + "is still going; the page shows the note once it is saved.",
                    ExitCode.GeneralFailure);
            }

            await Task.WhenAny(run, Task.Delay(AddPoll, _time, ct)).ConfigureAwait(false);
        }
    }

    /// <summary>The highest note number the idea has given, or -1 when it cannot be read.</summary>
    private async Task<int> NumberedAsync(IdeaPlace place, CancellationToken ct)
    {
        var read = await _ideas.ReadAsync(place, ct).ConfigureAwait(false);

        return read.Succeeded ? Math.Max(read.Value!.NotesNumbered, read.Value!.Notes.Count) : -1;
    }

    /// <summary>The command line a verb stands for, or an empty command for one there is none of.</summary>
    /// <remarks>
    /// <para>
    /// Its own function so a test can put every verb through the real parser:
    /// it is not enough that the command exists, every option on the line has
    /// to be one it declares.
    /// </para>
    /// <para>
    /// Prose goes joined to its option, <c>--answer=...</c>. The parser refuses
    /// a value that starts with a dash anywhere else, and prose from a text box
    /// often does. The list goes as <c>--project</c> or <c>--global</c>, named
    /// outright, because two lists can hold ideas of the same name.
    /// </para>
    /// </remarks>
    internal static (string Command, IReadOnlyList<string> Arguments) Maps(IdeaAction asking)
    {
        ArgumentNullException.ThrowIfNull(asking);

        string[] list = asking.Project is { Length: > 0 } project ? ["--project", project] : ["--global"];
        var id = asking.Id ?? string.Empty;

        (string, IReadOnlyList<string>) Line(string command, params IEnumerable<string>[] parts) =>
            (command, [.. parts.SelectMany(part => part), "--non-interactive"]);

        return asking.Verb switch
        {
            "add" => Line("idea add", [$"--text={asking.Text}"], list),
            "addto" => Line("idea add", ["--to", id, $"--text={asking.Text}"], asking.Refine ? ["--refine"] : [], list),
            "note-remove" => Line("idea note remove", [id, asking.Note ?? string.Empty], list),
            "refine" => Line("idea refine", [id], list),
            "answer" => Line("idea answer", [id, asking.Question ?? string.Empty, $"--answer={asking.Answer}"], list),
            "choose" => Line("idea choose", [id, asking.Layer ?? string.Empty, asking.Option ?? string.Empty], list),
            "keep" => Line("idea keep", [id], asking.Pieces ?? [], list),
            "drop" => Line("idea drop", [id], asking.Pieces ?? [], list),
            "improve" => Line("idea improve", [id, asking.Piece ?? string.Empty, $"--request={asking.Request}"], list),
            "accept" => Line(
                "idea accept",
                [id],
                list,
                asking.NewProject is { Length: > 0 } made ? [$"--new-project={made}"]
                    : asking.To is { Length: > 0 } to ? ["--to", to]
                    : []),
            "move" when DashboardActions.Destination(asking.To, asking.ToGlobal) is { } to =>
                Line("idea move", [id], list, to),
            "remove" => Line("idea remove", [id], list),
            "dump" => Line("idea dump add", [$"--text={asking.Text}"], list),
            "apply" => Line(
                "idea dump apply",
                [id],
                list,
                asking.Only is { Count: > 0 } only ? ["--only", string.Join(',', only)] : [],
                asking.To is { Length: > 0 } where ? ["--to", where] : []),
            _ => (string.Empty, []),
        };
    }
}
