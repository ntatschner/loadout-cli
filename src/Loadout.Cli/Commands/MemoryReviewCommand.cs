using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Instructions;
using Loadout.Core.Projects;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Models.Instructions;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>
/// Walks the topics nobody has revisited, one at a time.
/// </summary>
/// <remarks>
/// <para>
/// The audit has reported stale topics for as long as it has existed, and a
/// finding nobody acts on is a finding that trains people to skim the report.
/// Age is not falsity — a two-year-old fact about the build can be perfectly
/// true — so nothing here decides anything. It puts each one in front of
/// somebody with the question the audit could only imply.
/// </para>
/// <para>
/// Three answers and no fourth. Keep marks it as still true, which is the
/// commonest and has to be the cheapest. Expire deletes it, because a fact
/// somebody has looked at and judged false is worse than a missing one. Edit
/// says where the file is and steps aside: rewriting somebody's prose from a
/// prompt is how a review turns into a rewrite nobody asked for.
/// </para>
/// </remarks>
[Description("Walk the memory topics nobody has revisited, and keep or expire each.")]
[CommandMeta(CommandCategory.AgentConfiguration,
    Intent = "review stale memory confirm expire prune old topics", Mutates = true)]
public sealed class MemoryReviewCommand : AsyncCommand<MemoryReviewCommand.Settings>
{
    private readonly IMemoryService _memory;
    private readonly IProjectService _projects;
    private readonly IWorkspaceManager _workspace;
    private readonly IAnsiConsole _console;
    private readonly TimeProvider _time;

    public MemoryReviewCommand(
        IMemoryService memory,
        IProjectService projects,
        IWorkspaceManager workspace,
        IAnsiConsole console,
        TimeProvider time)
    {
        _memory = memory;
        _projects = projects;
        _workspace = workspace;
        _console = console;
        _time = time;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "[project]")]
        [Description("Project slug, alias or name. Defaults to the repository you are in.")]
        public string? Project { get; init; }

        [CommandOption("--older-than <MONTHS>")]
        [Description("How long since a topic was last written to count as unrevisited. Defaults to 6.")]
        public int OlderThan { get; init; } = 6;
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        if (settings.OlderThan <= 0)
        {
            return output.Fail("--older-than has to be at least 1.", ExitCode.InvalidArguments);
        }

        var resolution = settings.Project is { Length: > 0 } handle
            ? await _projects.ResolveAsync(handle, cancellationToken).ConfigureAwait(false)
            : await _projects.ResolveFromDirectoryAsync(
                settings.Repo ?? Directory.GetCurrentDirectory(), cancellationToken).ConfigureAwait(false);

        if (resolution.Failed)
        {
            return output.Fail(resolution);
        }

        var slug = resolution.Value!.Entry.Slug;

        var listed = await _memory.ListAsync(_workspace.LocalPath, slug, cancellationToken)
            .ConfigureAwait(false);

        if (listed.Failed)
        {
            return output.Fail(listed);
        }

        var stale = Unrevisited(listed.Value!, settings.OlderThan, _time.GetUtcNow());

        if (output.IsJson)
        {
            output.WriteJson(stale.Select(topic => new
            {
                topic.Name,
                topic.Description,
                topic.Path,
                scope = topic.Scope.ToString().ToLowerInvariant(),
                lastWritten = topic.WrittenUtc.ToString("yyyy-MM-dd"),
            }));

            return CommandOutput.Success();
        }

        if (stale.Count == 0)
        {
            output.WriteLine(
                $"[green]+[/] Nothing in {Markup.Escape(slug)}'s memory has gone "
                + $"{settings.OlderThan} month(s) without being revisited.");

            return CommandOutput.Success();
        }

        // Spec section 37: never a prompt where nobody can answer it. Listing
        // them is still worth something in a pipe — it is the audit's finding
        // with the topics named — so this reports rather than refusing.
        if (settings.NonInteractive || System.Console.IsInputRedirected || settings.DryRun)
        {
            output.WriteLine(
                $"[bold]{stale.Count}[/] topic(s) nobody has revisited in "
                + $"{settings.OlderThan} month(s):");
            output.WriteBlankLine();

            foreach (var topic in stale)
            {
                output.WriteLine(
                    $"  {Markup.Escape(topic.Name),-34} "
                    + $"[dim]{topic.WrittenUtc:yyyy-MM-dd}  {Markup.Escape(topic.Description)}[/]");
            }

            output.WriteBlankLine();
            output.WriteLine("[dim]Run this without a pipe to keep or expire each one.[/]");

            return CommandOutput.Success();
        }

        return await WalkAsync(output, slug, stale, cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> WalkAsync(
        CommandOutput output,
        string slug,
        IReadOnlyList<MemoryTopic> stale,
        CancellationToken ct)
    {
        var kept = 0;
        var expired = 0;

        foreach (var topic in stale)
        {
            output.WriteBlankLine();
            output.WriteLine(
                $"[bold]{Markup.Escape(topic.Name)}[/]  "
                + $"[dim]{topic.Scope.ToString().ToLowerInvariant()}, last written "
                + $"{topic.WrittenUtc:yyyy-MM-dd}[/]");
            output.WriteLine($"  [dim]{Markup.Escape(topic.Description)}[/]");

            // The facts themselves, because nobody can say whether a topic is
            // still true from its description. That line was written to help a
            // session decide whether to open it, not to stand in for it.
            foreach (var fact in topic.Facts.Take(4))
            {
                output.WriteLine($"  {Markup.Escape(Shorten(fact))}");
            }

            var answer = _console.Prompt(
                new SelectionPrompt<string>()
                    .Title("  Still true?")
                    .AddChoices(Keep, Expire, Edit, Stop));

            if (answer == Stop)
            {
                break;
            }

            if (answer == Edit)
            {
                // Said, not done. Rewriting somebody's prose from a prompt is
                // how a review becomes a rewrite nobody asked for.
                output.WriteLine($"  [dim]{Markup.Escape(topic.Path)}[/]");

                continue;
            }

            if (answer == Keep)
            {
                var touched = await TouchAsync(topic, ct).ConfigureAwait(false);

                if (touched is { Length: > 0 } problem)
                {
                    output.WriteLine($"  [yellow]{Markup.Escape(problem)}[/]");

                    continue;
                }

                kept++;

                continue;
            }

            try
            {
                File.Delete(topic.Path);
                expired++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                output.WriteLine($"  [yellow]Could not remove it: {Markup.Escape(ex.Message)}[/]");
            }
        }

        if (expired > 0)
        {
            // The index still lists what has gone. Rebuilding is the same
            // repair 'memory reindex' does, and leaving it would mean the next
            // session is offered topics that are not there.
            await _memory.RebuildIndexAsync(_workspace.LocalPath, slug, ct).ConfigureAwait(false);
        }

        output.WriteBlankLine();
        output.WriteLine($"[bold]{kept}[/] kept, [bold]{expired}[/] expired.");

        return CommandOutput.Success();
    }

    /// <summary>
    /// Marks a topic as still true by making it recently written.
    /// </summary>
    /// <remarks>
    /// The write time is the only record of when anybody last looked, and
    /// keeping a topic without moving it means the same question tomorrow. The
    /// file's contents are not touched: this records a judgement about the
    /// topic, not a change to it.
    /// </remarks>
    private async Task<string?> TouchAsync(MemoryTopic topic, CancellationToken ct)
    {
        try
        {
            File.SetLastWriteTimeUtc(topic.Path, _time.GetUtcNow().UtcDateTime);

            await Task.CompletedTask.ConfigureAwait(false);

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Could not mark it as reviewed: {ex.Message}";
        }
    }

    /// <summary>Topics nobody has written to for a while.</summary>
    internal static IReadOnlyList<MemoryTopic> Unrevisited(
        IReadOnlyList<MemoryTopic> topics,
        int months,
        DateTimeOffset now) =>
        topics
            .Where(topic => topic.WrittenUtc < now.AddMonths(-months))
            .OrderBy(topic => topic.WrittenUtc)
            .ToList();

    private static string Shorten(string fact) =>
        fact.Length <= 100 ? fact : fact[..100] + "...";

    private const string Keep = "keep — still true";
    private const string Expire = "expire — delete it";
    private const string Edit = "edit — show me where it is";
    private const string Stop = "stop";
}
