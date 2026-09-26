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
        ["add", "refine", "answer", "choose", "keep", "drop", "improve", "accept", "remove", "dump", "apply"];

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
                });
            }
        }

        var dumps = await _dumps.ListAsync(ct).ConfigureAwait(false);
        var projects = await _projects.ListAsync(ct).ConfigureAwait(false);

        return new
        {
            ideas,
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

        var key = asking.Verb switch
        {
            "refine" => asking.Id,
            "dump" => Dumping,
            _ => null,
        };

        if (key is null)
        {
            return Said(command, await _commands.RunAsync(command, arguments, ct).ConfigureAwait(false));
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
    private static OperationResult Said(string command, int code) =>
        code == (int)ExitCode.Success
            ? OperationResult.Ok()
            : OperationResult.Fail(
                (ExitCode)code switch
                {
                    ExitCode.PolicyViolation =>
                        "That looks like it contains a credential, so nothing was recorded. Say it without the value.",
                    ExitCode.ProjectNotFound =>
                        "That project is not one this workspace knows about.",
                    ExitCode.InvalidArguments =>
                        "The idea could not take that. It may have changed since the page last read it: the "
                        + "page has read it again. The terminal serving this page has the exact reason.",
                    _ => $"'{command}' ended with exit code {code}. The terminal serving this page has the reason.",
                },
                (ExitCode)code);

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
