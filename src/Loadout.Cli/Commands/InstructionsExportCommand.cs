using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Instructions;
using Loadout.Core.Projects;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>Options for copying a built-in specialist into the workspace.</summary>
public sealed class InstructionsExportSettings : GlobalSettings
{
    [CommandArgument(0, "<SPECIALIST>")]
    [Description("Identifier of the specialist to copy, such as language.rust.")]
    public string Specialist { get; init; } = string.Empty;

    [CommandOption("--project <PROJECT>")]
    [Description("Copy into one project rather than the whole workspace.")]
    public string? Project { get; init; }

    [CommandOption("--force")]
    [Description("Overwrite a copy that is already there.")]
    public bool Force { get; init; }
}

/// <summary>
/// Copies a built-in specialist into the workspace, where it can be edited,
/// reviewed and versioned like anything else.
/// </summary>
/// <remarks>
/// <para>
/// The shipped specialists are embedded in the binary, so changing one means a
/// release. Copying it into the workspace puts it in a repository somebody owns
/// — reviewable in a pull request, synced across their machines — and the
/// library already prefers a workspace copy over the built-in of the same id,
/// so nothing else has to change for it to take effect.
/// </para>
/// <para>
/// One at a time, deliberately. Exporting all seventy-one would freeze the lot:
/// a copy shadows the built-in for good, so a later release that improves the
/// original is silently ignored by anybody holding a copy of the old one.
/// That cost is worth paying for the handful somebody actually wants to change
/// and worth nothing for the rest.
/// </para>
/// <para>
/// The copy records which version it came from, so the staleness can be found
/// later rather than guessed at.
/// </para>
/// </remarks>
[Description("Copy a built-in specialist into the workspace so it can be edited and reviewed.")]
[CommandMeta(CommandCategory.AgentConfiguration,
    Intent = "customise override edit specialist copy out vendor", Mutates = true)]
public sealed class InstructionsExportCommand : AsyncCommand<InstructionsExportSettings>
{
    private readonly ISpecialistLibrary _library;
    private readonly IWorkspaceManager _workspace;
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;

    public InstructionsExportCommand(
        ISpecialistLibrary library,
        IWorkspaceManager workspace,
        IProjectService projects,
        IAnsiConsole console)
    {
        _library = library;
        _workspace = workspace;
        _projects = projects;
        _console = console;
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        InstructionsExportSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        if (!_workspace.IsAvailable())
        {
            return output.Fail(
                "There is no workspace on this machine to copy it into.",
                ExitCode.ConfigurationInvalid);
        }

        var text = _library.BuiltInText(settings.Specialist);

        if (text is null)
        {
            return output.Fail(
                $"'{settings.Specialist}' is not a built-in specialist. "
                + "See what there is with: loadout instructions list",
                ExitCode.ProjectNotFound);
        }

        var separator = settings.Specialist.IndexOf('.', StringComparison.Ordinal);
        var kind = settings.Specialist[..separator].ToLowerInvariant();
        var name = settings.Specialist[(separator + 1)..].ToLowerInvariant();

        string root;

        if (settings.Project is { Length: > 0 } handle)
        {
            var resolved = await _projects.ResolveAsync(handle, cancellationToken)
                .ConfigureAwait(false);

            if (resolved.Failed)
            {
                return output.Fail(resolved);
            }

            root = Path.Combine(
                _workspace.LocalPath, "projects", resolved.Value!.Entry.Slug, "specialists");
        }
        else
        {
            root = Path.Combine(_workspace.LocalPath, "global", "specialists");
        }

        var destination = Path.Combine(root, kind, name + ".md");

        if (File.Exists(destination) && !settings.Force)
        {
            return output.Fail(
                $"'{destination}' already exists. Pass --force to replace it, or edit it where "
                + "it is — it is already the one being used.",
                ExitCode.InvalidArguments);
        }

        // Stamped with where it came from. A copy shadows the built-in for
        // good, so a later release that improves the original is ignored by
        // anybody holding a copy — and without this there is nothing to compare
        // against to notice.
        //
        // Inside the frontmatter, as a YAML comment. Above it the file has no
        // frontmatter at all: the block has to open on the first line, and the
        // first attempt put an HTML comment there and produced a specialist the
        // library refused to load — which validate said plainly and the export
        // itself had not thought to check.
        var stamped = Stamp(text, settings.Specialist, ThisVersion());

        if (settings.DryRun)
        {
            output.WriteLine(
                $"[bold]Would write[/] {Markup.Escape(destination)} "
                + $"[dim]({stamped.Length / 1024 + 1}KB)[/]. Nothing was changed.");

            return CommandOutput.Success();
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        await File.WriteAllTextAsync(destination, stamped, cancellationToken).ConfigureAwait(false);

        output.WriteLine($"[green]Copied[/] {Markup.Escape(settings.Specialist)} to {Markup.Escape(destination)}");
        output.WriteLine(
            "[dim]It replaces the built-in of that id from now on, including when the built-in "
            + "improves. Commit it with: loadout workspace save[/]");

        return CommandOutput.Success();
    }

    /// <summary>
    /// Records the origin inside the frontmatter, where it survives parsing.
    /// </summary>
    private static string Stamp(string text, string id, string version)
    {
        var opening = text.IndexOf("---", StringComparison.Ordinal);

        if (opening < 0)
        {
            // Nothing to open, so nothing to write into. The library will
            // refuse this file anyway and say why.
            return text;
        }

        var after = text.IndexOf('\n', opening);

        if (after < 0)
        {
            return text;
        }

        // The fingerprint is what makes staleness answerable later. A copy
        // differing from the built-in proves nothing — differing is the reason
        // to make one. What matters is whether the built-in has moved since,
        // and that can only be known by recording what it looked like then.
        var note =
            $"# Copied from the {version} built-in library. Edit freely: this file replaces"
            + Environment.NewLine
            + $"# the built-in {id} wherever it applies, including when the built-in improves."
            + Environment.NewLine
            + $"# {SpecialistOrigins.Marker}{SpecialistOrigins.Fingerprint(text)}"
            + Environment.NewLine;

        return text[..(after + 1)] + note + text[(after + 1)..];
    }

    private static string ThisVersion() =>
        typeof(InstructionsExportCommand).Assembly.GetName().Version?.ToString(3) ?? "unknown";
}
