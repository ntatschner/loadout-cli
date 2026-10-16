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

/// <summary>Prints a topic exactly as it is stored, with the version an edit has to name.</summary>
[Description("Print a memory topic exactly as it is stored, with its version.")]
[CommandMeta(CommandCategory.AgentConfiguration, Intent = "memory show topic text read version")]
public sealed class MemoryShowCommand : MemoryCommandBase<MemoryShowCommand.Settings>
{
    private readonly IMemoryService _memory;

    public MemoryShowCommand(IMemoryService memory, IProjectService projects, IWorkspaceManager workspace, IAnsiConsole console)
        : base(projects, workspace, console) => _memory = memory;

    public sealed class Settings : MemorySettings
    {
        [CommandArgument(0, "<topic>")]
        [Description("The topic, as 'loadout memory list' names it.")]
        public string Topic { get; init; } = string.Empty;

        [CommandOption("--project <SLUG>")]
        [Description("Project slug, alias or name. Defaults to the repository you are in.")]
        public override string? Project { get; init; }

        [CommandOption("--scope <SCOPE>")]
        [Description("project (the default), user, or machine.")]
        public string? Scope { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(Console, settings);

        if (MemoryText.Scope(settings.Scope) is not { } scope)
        {
            return output.Fail($"'{settings.Scope}' is not a scope. Use project, user or machine.", ExitCode.InvalidArguments);
        }

        var slug = await ResolveSlugAsync(settings).ConfigureAwait(false);

        if (slug.Failed)
        {
            return output.Fail(slug);
        }

        var read = await _memory.ReadTextAsync(Workspace.LocalPath, slug.Value!, settings.Topic, scope, cancellationToken)
            .ConfigureAwait(false);

        if (read.Failed)
        {
            return output.Fail(read);
        }

        if (output.IsJson)
        {
            output.WriteJson(MemoryText.Json(slug.Value!, read.Value!));

            return CommandOutput.Success();
        }

        // As it is, with nothing added, so it can be redirected to a file,
        // edited and handed to 'memory replace'. The version goes to stderr
        // for the same reason.
        Console.Profile.Out.Writer.Write(read.Value!.Text);
        System.Console.Error.WriteLine($"version {read.Value.Version}");

        return CommandOutput.Success();
    }
}

/// <summary>Replaces a topic's whole text, unless it changed since the version named.</summary>
[Description("Replace a memory topic's whole text, unless it changed since you read it.")]
[CommandMeta(CommandCategory.AgentConfiguration, Intent = "memory replace edit topic text save", Example = "restart-manager --file topic.md --expect <version>")]
public sealed class MemoryReplaceCommand : MemoryCommandBase<MemoryReplaceCommand.Settings>
{
    private readonly IMemoryService _memory;

    public MemoryReplaceCommand(IMemoryService memory, IProjectService projects, IWorkspaceManager workspace, IAnsiConsole console)
        : base(projects, workspace, console) => _memory = memory;

    public sealed class Settings : MemorySettings
    {
        [CommandArgument(0, "<topic>")]
        [Description("The topic, as 'loadout memory list' names it.")]
        public string Topic { get; init; } = string.Empty;

        [CommandOption("--project <SLUG>")]
        [Description("Project slug, alias or name. Defaults to the repository you are in.")]
        public override string? Project { get; init; }

        [CommandOption("--scope <SCOPE>")]
        [Description("project (the default), user, or machine.")]
        public string? Scope { get; init; }

        [CommandOption("--file <PATH>")]
        [Description("The new text, frontmatter and all.")]
        public string? File { get; init; }

        [CommandOption("--expect <VERSION>")]
        [Description("The version you edited, from 'loadout memory show'. Nothing is written if the topic has changed since.")]
        public string? Expect { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(Console, settings);

        if (MemoryText.Scope(settings.Scope) is not { } scope)
        {
            return output.Fail($"'{settings.Scope}' is not a scope. Use project, user or machine.", ExitCode.InvalidArguments);
        }

        // Both asked for rather than defaulted. Without the version there is
        // no telling whether somebody else wrote the topic first, and an edit
        // that cannot tell is the one that loses theirs.
        if (settings.File is not { Length: > 0 } file || settings.Expect is not { Length: > 0 } expect)
        {
            return output.Fail(
                "Name the new text with --file and the version you edited with --expect. "
                + "'loadout memory show <topic>' prints both the text and its version.",
                ExitCode.InvalidArguments);
        }

        string text;

        try
        {
            text = await System.IO.File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return output.Fail($"Could not read '{file}': {ex.Message}", ExitCode.InvalidArguments);
        }

        var slug = await ResolveSlugAsync(settings).ConfigureAwait(false);

        if (slug.Failed)
        {
            return output.Fail(slug);
        }

        var replaced = await _memory
            .ReplaceTextAsync(Workspace.LocalPath, slug.Value!, settings.Topic, scope, text, expect, settings.DryRun, cancellationToken)
            .ConfigureAwait(false);

        if (replaced.Failed)
        {
            return output.Fail(replaced);
        }

        var result = replaced.Value!;

        if (result.Conflicted)
        {
            return output.Fail(
                $"'{result.Topic.Topic.Name}' has changed since that version was read, so nothing was written. "
                + $"It is now version {result.Topic.Version}: read it again with 'loadout memory show' and redo the edit on that.",
                ExitCode.ChangedSinceRead);
        }

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                applied = !settings.DryRun,
                topic = MemoryText.Json(slug.Value!, result.Topic),
            });

            return CommandOutput.Success();
        }

        output.WriteLine(settings.DryRun
            ? $"Would replace [bold]{Markup.Escape(result.Topic.Topic.Name)}[/]. [dim]Nothing was written.[/]"
            : $"[green]Replaced[/] {Markup.Escape(result.Topic.Topic.Name)}. [dim]It is now version {result.Topic.Version}.[/]");

        return CommandOutput.Success();
    }
}

/// <summary>What the two text commands share.</summary>
internal static class MemoryText
{
    /// <summary>The store named, or null where it names none there is.</summary>
    public static MemoryScope? Scope(string? named) =>
        named is null or ""
            ? MemoryScope.Project
            : Enum.TryParse<MemoryScope>(named, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) ? parsed : null;

    public static object Json(string slug, MemoryTopicText topic) => new
    {
        project = slug,
        topic = topic.Topic.Name,
        scope = topic.Topic.Scope.ToString().ToLowerInvariant(),
        description = topic.Topic.Description,
        kind = topic.Topic.Kind.ToString().ToLowerInvariant(),
        version = topic.Version,
        text = topic.Text,
    };
}
