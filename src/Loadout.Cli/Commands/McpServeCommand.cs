using System.Text;
using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Instructions;
using Loadout.Core.Projects;
using Loadout.Core.Workspace;
using Loadout.Core.Git;
using Loadout.Models.Instructions;
using Loadout.Tui;
using System.Reflection;
using Loadout.Core;
using Loadout.Platform;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>Options for serving the launcher's own tools to an agent.</summary>
public sealed class McpServeSettings : GlobalSettings
{
    [CommandOption("--project <PROJECT>")]
    [Description("Project the tools answer about. Defaults to the repository the agent is in.")]
    public string? Project { get; init; }
}

/// <summary>
/// Serves a few of the launcher's own operations to the agent it launched.
/// </summary>
/// <remarks>
/// <para>
/// The handoff has been one way: the launcher composes a context, starts an
/// agent and hears nothing more. Telling the agent that <c>loadout</c> is on
/// PATH closed half of that — it can shell out and read the text back. This
/// closes the other half, so a session can ask for a specialist or record what
/// it learned without parsing console output written for a person.
/// </para>
/// <para>
/// Deliberately a small surface, and the same one the compiled context names:
/// reading what this session was given, reading a specialist in full, searching
/// what the project already knows, and writing one fact to memory. Nothing here
/// pushes to a remote, changes the machine, or starts an agent. A tool an agent
/// can call unprompted is a decision made without anybody watching, so the set
/// is the part of the launcher where that is safe.
/// </para>
/// <para>
/// Speaks JSON-RPC over stdin and stdout, so nothing it writes may go to
/// standard output but the protocol. The logging is pointed at stderr for
/// exactly that reason: a stray line on stdout is a malformed message, and the
/// session ends without saying why.
/// </para>
/// </remarks>
[Description("Serve the launcher's own tools to an agent over MCP.")]
[CommandMeta(CommandCategory.Integration,
    Intent = "mcp server tools agent callback two way")]
public sealed class McpServeCommand : AsyncCommand<McpServeSettings>
{
    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        McpServeSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // The launcher's own container rather than a generic host: everything
        // else here is built this way, and a hosting stack for one command is
        // a great deal of machinery to carry in a self-contained binary.
        var services = new ServiceCollection();

        services.AddPlatformServices().AddCoreServices();
        services.AddSingleton(new LoadoutToolScope(settings.Project));
        services.AddSingleton<LoadoutTools>();

        using var provider = services.BuildServiceProvider();

        var tools = new McpServerPrimitiveCollection<McpServerTool>();

        // Built from the methods carrying the attribute, against the single
        // instance the container holds, so each tool is the same call the
        // equivalent command makes.
        var instance = provider.GetRequiredService<LoadoutTools>();

        foreach (var method in typeof(LoadoutTools).GetMethods(
            BindingFlags.Public | BindingFlags.Instance))
        {
            if (method.GetCustomAttribute<McpServerToolAttribute>() is null)
            {
                continue;
            }

            tools.Add(McpServerTool.Create(method, instance));
        }

        var options = new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "loadout", Version = Version() },
            Capabilities = new ServerCapabilities { Tools = new ToolsCapability() },
            ToolCollection = tools,
        };

        // Nothing may reach standard output but the protocol: a stray line is a
        // malformed message, and the session ends without saying why. No logger
        // is passed for that reason.
        await using var transport = new StdioServerTransport("loadout");
        await using var server = McpServer.Create(transport, options, loggerFactory: null, provider);

        await server.RunAsync(cancellationToken).ConfigureAwait(false);

        return CommandOutput.Success();
    }

    private static string Version() =>
        typeof(McpServeCommand).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}

/// <summary>Which project the served tools answer about.</summary>
/// <param name="Project">Project handle, or null to work it out from the repository.</param>
public sealed record LoadoutToolScope(string? Project);

/// <summary>
/// The launcher operations an agent may call for itself.
/// </summary>
/// <remarks>
/// Each one is the same call the equivalent command makes. A second
/// implementation of any of this would drift from what the command line does,
/// and then an agent and a person would be told different things about the same
/// project.
/// </remarks>
[McpServerToolType]
public sealed class LoadoutTools
{
    private readonly IInstructionService _instructions;
    private readonly IMemoryService _memory;
    private readonly IWorkspaceManager _workspace;
    private readonly IProjectService _projects;
    private readonly Core.Tasks.ITaskService _tasks;
    private readonly IGitManager _git;
    private readonly TimeProvider _time;
    private readonly LoadoutToolScope _scope;

    public LoadoutTools(
        IInstructionService instructions,
        IMemoryService memory,
        IWorkspaceManager workspace,
        IProjectService projects,
        Core.Tasks.ITaskService tasks,
        IGitManager git,
        TimeProvider time,
        LoadoutToolScope scope)
    {
        _instructions = instructions;
        _memory = memory;
        _workspace = workspace;
        _projects = projects;
        _tasks = tasks;
        _git = git;
        _time = time;
        _scope = scope;
    }

    [McpServerTool(Name = "loadout_specialist")]
    [Description(
        "The full text of one specialist the launcher knows about, with what makes it apply. "
        + "Use it when the compiled context names a specialist and you want what it actually says.")]
    public string Specialist(
        [Description("Identifier, such as language.rust or foundation.engineering-core.")]
        string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        return _instructions.BuiltInText(id)
            ?? $"There is no specialist called '{id}'.";
    }

    [McpServerTool(Name = "loadout_effective_instructions")]
    [Description(
        "What this session was given and why: the specialists that applied, and what triggered "
        + "each. Use it to find out what you already have before asking for more.")]
    public async Task<string> EffectiveInstructionsAsync(CancellationToken ct = default)
    {
        var slug = await SlugAsync(ct).ConfigureAwait(false);

        if (slug is null)
        {
            return "No project could be worked out from here.";
        }

        var manifest = await _workspace.ReadProjectAsync(slug, ct).ConfigureAwait(false);

        var resolved = await _instructions.ResolveAsync(
            new InstructionRequest(
                manifest.Value,
                RepositoryPath: null,
                _workspace.LocalPath,
                manifest.Value?.Agents.Default ?? "claude"),
            ct).ConfigureAwait(false);

        if (resolved.Failed)
        {
            return resolved.Error ?? "The instructions could not be resolved.";
        }

        return string.Join(
            Environment.NewLine,
            resolved.Value!.Selected.Select(s => $"{s.Specialist.Id} - {s.Reason}"));
    }

    [McpServerTool(Name = "loadout_recall")]
    [Description(
        "Look for what this project already knows about something, before working it out again. "
        + "The context carries only a one-line index of memory topics; this searches what is "
        + "inside them. Matches words rather than meanings, so try the words the project would "
        + "use. Ask before recording a fact, so an existing topic is extended rather than "
        + "contradicted by a second one beside it.")]
    public async Task<string> RecallAsync(
        [Description("What you want to know, in your own words.")] string query,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var slug = await SlugAsync(ct).ConfigureAwait(false);

        if (slug is null)
        {
            return "No project could be worked out from here, so there is no memory to search.";
        }

        var listed = await _memory.ListAsync(_workspace.LocalPath, slug, ct).ConfigureAwait(false);

        if (listed.Failed)
        {
            return listed.Error ?? "The memory store could not be read.";
        }

        var matches = MemorySearch.Rank(listed.Value!, query);

        if (matches.Count == 0)
        {
            // Said plainly, because an agent told "nothing found" will otherwise
            // record what it has just worked out as though it were new.
            return "Nothing matched those words. It searches words rather than meanings, so "
                + "the project may hold this under different ones.";
        }

        var answer = new StringBuilder();

        foreach (var match in matches)
        {
            answer.AppendLine($"{match.Topic.Name} - {match.Topic.Description}");

            // The facts themselves, not a summary of them. Nothing here reworks
            // what somebody wrote down; a summarised memory is a memory that can
            // say something its source did not.
            foreach (var fact in match.Matched.Count > 0 ? match.Matched : match.Topic.Facts)
            {
                answer.AppendLine($"  - {fact}");
            }
        }

        return answer.ToString().TrimEnd();
    }

    [McpServerTool(Name = "loadout_remember")]
    [Description(
        "Record one durable fact about this project, so the next session starts with it. "
        + "For things that stay true: a decision and why, a constraint, a trap. Not for "
        + "anything secret - the text is screened and a credential is refused.")]
    public async Task<string> RememberAsync(
        [Description("Short topic name, such as 'deploy' or 'schema'.")] string topic,
        [Description("The fact, in a sentence or two. Say what is true, not what you just did.")]
        string fact,
        [Description(
            "One line saying what question this topic answers, such as 'why installers fail with "
            + "1603 over a running app'. It is the only thing a later session sees before "
            + "deciding whether to open the topic, so it has to be worth reading on its own.")]
        string description,
        [Description(
            "Only after being told existing topics already cover this ground, and having decided "
            + "this really is a separate subject. Prefer recording the fact under one of the "
            + "topics named back to you: a second topic beside the first is how memory comes to "
            + "hold two answers with nothing to choose between them.")]
        bool separate = false,
        [Description(
            "project (the default), user for something true of your work whatever the project, "
            + "or machine for something true only of this computer. A machine fact recorded as "
            + "a project one is a fact that syncs to machines it is false on.")]
        string scope = "project",
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentException.ThrowIfNullOrWhiteSpace(fact);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        var slug = await SlugAsync(ct).ConfigureAwait(false);

        if (slug is null)
        {
            return "No project could be worked out from here, so there is nowhere to record it.";
        }

        // The same call 'loadout memory write' makes, screening included. A
        // credential reaching the workspace because an agent wrote it rather
        // than a person is the same disclosure either way.
        var written = await _memory
            .WriteAsync(
                _workspace.LocalPath,
                slug,
                topic,

                // Asked for rather than generated. What was written here before
                // said an agent had recorded something, which is the one thing a
                // later session can already see; the index line it produced was
                // paid for on every launch and could not be chosen from.
                description,
                MemoryKind.Lesson,
                [fact],
                separate,
                Scope(scope),
                ct)
            .ConfigureAwait(false);

        return written.Succeeded
            ? $"Recorded under '{topic}'."
            : written.Error ?? "It could not be recorded.";
    }

    /// <summary>A named scope, defaulting to the project when it is not one we know.</summary>
    private static MemoryScope Scope(string? name) =>
        Enum.TryParse<MemoryScope>(name, ignoreCase: true, out var parsed)
            ? parsed
            : MemoryScope.Project;

    [McpServerTool(Name = "loadout_tasks")]
    [Description(
        "What this project is working on: every open task, who said so and when, plus anything "
        + "the repository does not back up. Use it to answer \"where were we\" from the record "
        + "rather than from the last thing in context.")]
    public async Task<string> TasksAsync(CancellationToken ct = default)
    {
        var slug = await SlugAsync(ct).ConfigureAwait(false);

        if (slug is null)
        {
            return "No project could be worked out from here.";
        }

        var listed = await _tasks.ListAsync(slug, ct).ConfigureAwait(false);

        if (listed.Failed)
        {
            return listed.Error ?? "The tasks could not be read.";
        }

        if (listed.Value!.Count == 0)
        {
            return $"Nothing is recorded for {slug}.";
        }

        var now = _time.GetUtcNow();
        var lines = new List<string>();

        foreach (var item in listed.Value!
            .OrderBy(item => item.State)
            .ThenBy(item => item.Id, StringComparer.Ordinal))
        {
            lines.Add(
                $"{item.Id} [{item.State.ToString().ToLowerInvariant()}] {item.Title}"
                + $" - said by {(item.DeclaredBy.Length > 0 ? item.DeclaredBy : "nobody named")}"
                + $" on {item.DeclaredUtc:yyyy-MM-dd}");
        }

        // The disagreements travel with the answer rather than being available
        // separately. A session handed its own claim back as fact is the thing
        // this exists to prevent: it is the one reader that cannot tell the
        // difference, because it is usually the one that made the claim.
        var unsupported = await CheckAsync(slug, listed.Value!, now, ct).ConfigureAwait(false);

        foreach (var disagreement in unsupported)
        {
            lines.Add($"  unsupported: {disagreement.TaskId} {disagreement.Detail}");
        }

        var composed = Core.Tasks.Suggestions.Compose(listed.Value!, unsupported);

        if (composed.Count > 0)
        {
            // Labelled as composed, and kept apart from anything the session
            // writes itself. These were assembled out of the states above, so
            // they cannot be wrong about what they name; a reply the agent
            // drafts can be confidently wrong about the same thing, and the
            // only defence is that the two never arrive as one list.
            lines.Add(string.Empty);
            lines.Add("Composed from the record above - these cannot be wrong about what they name:");

            foreach (var suggestion in composed)
            {
                lines.Add($"  {suggestion.Text}");
            }

            lines.Add(
                "Anything you suggest beyond these is your own draft. Say so when you offer it, "
                + "and offer it - none of this is done for you.");
        }

        return string.Join(Environment.NewLine, lines);
    }

    [McpServerTool(Name = "loadout_task_declare")]
    [Description(
        "Record where a task stands: open, doing, done, blocked or dropped. Adds it when the id "
        + "is new. This records a claim, attributed and dated; it does not make the claim true.")]
    public async Task<string> DeclareTaskAsync(
        [Description("Short identifier for the task.")] string id,
        [Description("open, doing, done, blocked or dropped.")] string state,
        [Description("What the work is. Leave out to keep what is there.")] string? title = null,
        [Description("Anything worth adding.")] string? note = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(state);

        if (!Enum.TryParse<Models.Tasks.TaskState>(state, ignoreCase: true, out var parsed))
        {
            return $"There is no '{state}' state. The states are: open, doing, done, blocked, dropped.";
        }

        var slug = await SlugAsync(ct).ConfigureAwait(false);

        if (slug is null)
        {
            return "No project could be worked out from here.";
        }

        var declared = await _tasks
            .DeclareAsync(slug, id, parsed, "agent", title, note, ct)
            .ConfigureAwait(false);

        return declared.Succeeded
            ? $"Recorded {declared.Value!.Id} as {parsed.ToString().ToLowerInvariant()}, "
                + "attributed to this session and dated now."
            : declared.Error ?? "The task could not be recorded.";
    }

    /// <summary>What the repository has to say about the claims.</summary>
    /// <remarks>
    /// A repository that cannot be read returns nothing rather than an empty
    /// history: with no commits, "nothing committed since" would fire on every
    /// task at once, which is a confidently wrong answer where none was needed.
    /// </remarks>
    private async Task<IReadOnlyList<Core.Tasks.TaskDisagreement>> CheckAsync(
        string slug,
        IReadOnlyList<Models.Tasks.TaskItem> tasks,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var resolved = await _projects.ResolveAsync(slug, ct).ConfigureAwait(false);

        if (resolved.Failed
            || resolved.Value!.LocalPath is not { Length: > 0 } path
            || !Directory.Exists(path))
        {
            return [];
        }

        var oldest = tasks
            .Where(item => item.State is Models.Tasks.TaskState.Done or Models.Tasks.TaskState.Doing)
            .Select(item => item.DeclaredUtc)
            .DefaultIfEmpty(now)
            .Min();

        var commits = await _git.ListCommitsAsync(path, oldest, ct).ConfigureAwait(false);

        return commits.Succeeded
            ? Core.Tasks.TaskCorroboration.Check(tasks, commits.Value!, now)
            : [];
    }

    [McpServerTool(Name = "loadout_mode")]
    [Description(
        "Switch the posture for the rest of this session, and get what that changes. A mode is "
        + "a session-wide directive, not a per-message one: adopt what this returns and keep to "
        + "it until the work changes shape again. Use it when what you are doing stops matching "
        + "how the session started - asked to look into a bug and now writing the fix, or the "
        + "other way round.")]
    public async Task<string> ModeAsync(
        [Description("advise, investigate, implement or review.")] string mode,
        [Description("What you are about to do, so task-triggered specialists are chosen for it.")]
        string? task = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mode);

        var slug = await SlugAsync(ct).ConfigureAwait(false);

        var catalogue = await _instructions
            .LibraryAsync(_workspace.LocalPath, slug, ct)
            .ConfigureAwait(false);

        var asked = mode.Trim().ToLowerInvariant();

        var known = catalogue
            .OfKind(SpecialistKind.Mode)
            .Select(m => m.Id.StartsWith("mode.", StringComparison.Ordinal) ? m.Id[5..] : m.Id)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        // The resolver falls back to the default for a name it does not know,
        // which is right for a command line and wrong here: an agent told
        // nothing would carry on believing it had switched.
        if (!known.Contains(asked, StringComparer.Ordinal))
        {
            return $"There is no '{asked}' mode. The modes are: {string.Join(", ", known)}.";
        }

        var posture = _instructions.BuiltInText($"mode.{asked}");

        if (slug is null)
        {
            return posture ?? $"The {asked} mode has no text.";
        }

        var manifest = await _workspace.ReadProjectAsync(slug, ct).ConfigureAwait(false);

        var resolved = await _instructions.ResolveAsync(
            new InstructionRequest(
                manifest.Value,
                RepositoryPath: null,
                _workspace.LocalPath,
                manifest.Value?.Agents.Default ?? "claude",
                Task: task,
                Mode: asked),
            ct).ConfigureAwait(false);

        if (resolved.Failed)
        {
            return resolved.Error ?? "The instructions could not be resolved.";
        }

        var applying = string.Join(
            Environment.NewLine,
            resolved.Value!.Selected.Select(s => $"  {s.Specialist.Id} - {s.Reason}"));

        return $"""
            Now in {asked} mode for the rest of this session.

            {posture}

            What applies now:
            {applying}

            The language and framework specialists come from the repository, so they do not
            change with the mode. What changes is the posture above, and the skills a mode
            allows: a reviewing skill is offered in investigate, advise and review, and
            withheld from implement.
            """;
    }

    private async Task<string?> SlugAsync(CancellationToken ct)
    {
        if (_scope.Project is { Length: > 0 } named)
        {
            var resolved = await _projects.ResolveAsync(named, ct).ConfigureAwait(false);

            return resolved.Succeeded ? resolved.Value!.Entry.Slug : null;
        }

        var here = await _projects
            .ResolveFromDirectoryAsync(Directory.GetCurrentDirectory(), ct)
            .ConfigureAwait(false);

        return here.Succeeded ? here.Value!.Entry.Slug : null;
    }
}
