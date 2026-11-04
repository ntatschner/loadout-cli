using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Capabilities;
using Loadout.Core.Projects;
using Loadout.Models;
using Loadout.Models.Capabilities;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>What a project lacks before a piece of work, and taking up the setup Loadout offers for it.</summary>
/// <remarks>
/// <para>
/// Without <c>--accept</c> it changes nothing and reaches no network. Accepting
/// builds the profile afresh, makes one prerequisite task from a shipped
/// template, frozen and versioned, and makes the work require it.
/// </para>
/// <para>
/// Studio's "Before this" band runs this same command, so there is one way an
/// offer is taken up and it can be run from a terminal as written.
/// </para>
/// </remarks>
[Description("What a project lacks before a task or idea, such as CI or a release workflow. --accept makes the prerequisite task and links it.")]
[CommandMeta(CommandCategory.Workspace,
    Intent = "offers offer gaps missing before prerequisite ci release environments template setup capability",
    Mutates = true,
    Example = "release-notes --project loadout-cli --accept ci")]
public sealed class TaskOffersCommand : AsyncCommand<TaskOffersCommand.Settings>
{
    private readonly IProjectService _projects;
    private readonly ICapabilityOffers _offers;
    private readonly IAnsiConsole _console;

    public TaskOffersCommand(IProjectService projects, ICapabilityOffers offers, IAnsiConsole console)
    {
        _projects = projects;
        _offers = offers;
        _console = console;
    }

    public sealed class Settings : TaskSettings
    {
        [CommandArgument(0, "<id>")]
        [Description("The task or idea that would wait.")]
        public string Id { get; init; } = string.Empty;

        [CommandOption("--accept <CHECK>")]
        [Description("Take up the offer for this check: ci, release or environments.")]
        public string? Accept { get; init; }

        [CommandOption("--stack <STACK>")]
        [Description("With --accept: which stack to write it for, instead of the default. 'all' or 'generic' where they are offered.")]
        public string? Stack { get; init; }

        [CommandOption("--env-name <NAME>")]
        [Description("With --accept environments: a name to set up, instead of those last given. Repeat it for more than one.")]
        public string[] Environments { get; init; } = [];
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);
        var resolution = await settings.ListAsync(_projects, cancellationToken).ConfigureAwait(false);

        if (resolution.Failed)
        {
            return output.Fail(resolution);
        }

        if (resolution.Value is not { } project)
        {
            return output.Fail("Offers come from what a project has, so the workspace-wide list has none. Say which with --project.", ExitCode.InvalidArguments);
        }

        if (settings.Accept is not { Length: > 0 } check)
        {
            if (settings.Stack is not null || settings.Environments.Length > 0)
            {
                return output.Fail("--stack and --env-name go with --accept.", ExitCode.InvalidArguments);
            }

            var read = await _offers.ReadAsync(project, settings.Id, cancellationToken).ConfigureAwait(false);

            return read.Failed ? output.Fail(read) : Show(output, read.Value!);
        }

        if (settings.DryRun)
        {
            output.WriteLine($"Would make the {Markup.Escape(check)} prerequisite and make {Markup.Escape(settings.Id)} require it. Nothing was written.");

            return CommandOutput.Success();
        }

        var accepted = await _offers.AcceptAsync(
            project,
            settings.Id,
            check,
            settings.Stack,
            settings.Environments.Length > 0 ? settings.Environments : null,
            Environment.UserName,
            cancellationToken).ConfigureAwait(false);

        if (accepted.Failed)
        {
            return output.Fail(accepted);
        }

        var task = accepted.Value!.Prerequisite;

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                project = project.Entry.Slug,
                waiting = settings.Id,
                prerequisite = task.Id,
                created = accepted.Value.Created,
                check = task.Prerequisite?.Check,
                templateVersion = task.Prerequisite?.TemplateVersion,
                stacks = task.Prerequisite?.Stacks,
                environments = task.Prerequisite?.Environments,
            });

            return CommandOutput.Success();
        }

        output.WriteLine(
            accepted.Value.Created
                ? $"[green]+[/] {Markup.Escape(task.Id)}: {Markup.Escape(task.Title)}"
                : $"Reusing the open task {Markup.Escape(task.Id)}.");
        output.WriteLine($"{Markup.Escape(settings.Id)} now requires it, and is held out of team queues until it is done.");

        return CommandOutput.Success();
    }

    private static int Show(CommandOutput output, OfferSheet sheet)
    {
        if (output.IsJson)
        {
            output.WriteJson(new
            {
                project = sheet.Project,
                task = sheet.Task,
                built = sheet.Built,
                github = sheet.GitHub,
                offers = sheet.Offers.Select(offer => new
                {
                    check = offer.Check,
                    name = offer.Name,
                    state = ProjectCapabilitiesCommand.Name(offer.State),
                    evidence = offer.Evidence,
                    canOffer = offer.CanOffer,
                    stacks = offer.Stacks,
                    environments = offer.Environments,
                    prerequisite = offer.Prerequisite,
                    linked = offer.Linked,
                }),
                unseen = sheet.Unseen.Select(one => new { check = one.Check, state = ProjectCapabilitiesCommand.Name(one.State), hint = one.Hint }),
                notNeeded = sheet.NotNeeded,
                notes = sheet.Notes,
                links = (sheet.Links ?? []).Select(link => new { check = link.Check, prerequisite = link.Prerequisite, outgrown = link.Outgrown }),
            });

            return CommandOutput.Success();
        }

        if (sheet.Offers.Count == 0 && sheet.Unseen.Count == 0)
        {
            output.WriteLine($"Nothing is missing before {Markup.Escape(sheet.Task)}, as far as the profile has seen.");
        }

        foreach (var offer in sheet.Offers)
        {
            output.WriteLine($"[bold]{Markup.Escape(offer.Check)}[/]  {Markup.Escape(offer.Evidence)}");

            if (offer.Linked)
            {
                output.WriteLine($"[dim]  Already waiting for {Markup.Escape(offer.Prerequisite!)}.[/]");
            }
            else if (offer.CanOffer)
            {
                var stacks = offer.Stacks.Count > 0 ? $" Stacks: {string.Join(", ", offer.Stacks)}." : string.Empty;

                output.WriteLine($"[dim]  Take it: loadout task offers {Markup.Escape(sheet.Task)} --project {Markup.Escape(sheet.Project)} --accept {Markup.Escape(offer.Check)}.{Markup.Escape(stacks)}[/]");
            }
            else
            {
                output.WriteLine("[dim]  No offer: only Loadout's own templates make one. Link a task by hand with loadout task requires.[/]");
            }

            output.WriteLine($"[dim]  Or: loadout project capabilities {Markup.Escape(sheet.Project)} --not-needed {Markup.Escape(offer.Check)}[/]");
        }

        foreach (var link in (sheet.Links ?? []).Where(one => one.Outgrown))
        {
            output.WriteLine($"[yellow]{Markup.Escape(sheet.Task)} still waits for {Markup.Escape(link.Prerequisite)}[/], which its type no longer depends on.");
            output.WriteLine($"[dim]  Stop: loadout task requires {Markup.Escape(sheet.Task)} {Markup.Escape(link.Prerequisite)} --project {Markup.Escape(sheet.Project)} --remove[/]");
        }

        foreach (var one in sheet.Unseen)
        {
            output.WriteLine($"[dim]{Markup.Escape(one.Check)} could not be looked at. {Markup.Escape(one.Hint)}[/]");
        }

        return CommandOutput.Success();
    }
}
