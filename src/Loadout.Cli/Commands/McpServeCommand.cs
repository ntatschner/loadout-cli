using System.Text;
using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Instructions;
using Loadout.Core.Projects;
using Loadout.Core.Workspace;
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
    private readonly LoadoutToolScope _scope;

    public LoadoutTools(
        IInstructionService instructions,
        IMemoryService memory,
        IWorkspaceManager workspace,
        IProjectService projects,
        LoadoutToolScope scope)
    {
        _instructions = instructions;
        _memory = memory;
        _workspace = workspace;
        _projects = projects;
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
                ct)
            .ConfigureAwait(false);

        return written.Succeeded
            ? $"Recorded under '{topic}'."
            : written.Error ?? "It could not be recorded.";
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
