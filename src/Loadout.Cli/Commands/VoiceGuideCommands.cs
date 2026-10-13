using System.ComponentModel;
using System.Globalization;
using Loadout.Agents.Voice;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Voice;
using Loadout.Models;
using Loadout.Models.Voice;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>Options for drafting a guide.</summary>
public sealed class VoiceGuideDraftSettings : GlobalSettings
{
    [CommandArgument(0, "<ID>")]
    [Description("The guide's id, such as idiolect or architecture.")]
    public string Id { get; init; } = string.Empty;

    [CommandOption("--about <TEXT>")]
    [Description("What the guide should learn, in your own words.")]
    public string? About { get; init; }

    [CommandOption("--model <MODEL>")]
    [Description("The model to ask for, as the agent spells it.")]
    public string? Model { get; init; }
}

/// <summary>
/// Drafts a guide definition, shows it, and saves it only when you say so.
/// </summary>
/// <remarks>
/// <para>
/// The meta step. A guide is drafted once and then fixed, so what every run
/// of it produces is something other features can rely on. The draft is one
/// model call with no tools; reviewing it is the person's job, so saving
/// needs a terminal and there is no flag to skip the question.
/// </para>
/// <para>
/// A saved version is never replaced. Drafting the same id again proposes the
/// next version, and a profile learned against the old one keeps its meaning.
/// </para>
/// </remarks>
[Description("Draft a new guide definition with an agent, review it, and save it as a fixed version.")]
[CommandMeta(CommandCategory.AgentConfiguration,
    Intent = "draft guide definition voice idiolect architecture language preferences meta",
    Mutates = true,
    Example = "voice guide draft idiolect --about \"the words and phrases I reach for\"")]
public sealed class VoiceGuideDraftCommand : AsyncCommand<VoiceGuideDraftSettings>
{
    private readonly IVoiceGuideDrafter _drafter;
    private readonly VoiceStore _store;
    private readonly IAnsiConsole _console;

    public VoiceGuideDraftCommand(IVoiceGuideDrafter drafter, VoiceStore store, IAnsiConsole console)
    {
        _drafter = drafter;
        _store = store;
        _console = console;
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        VoiceGuideDraftSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        if (output.IsJson && !settings.DryRun)
        {
            return output.Fail(
                "Saving a draft asks you first, so --json goes with --dry-run.", ExitCode.InvalidArguments);
        }

        // Refused before anything is spent: a draft nobody can review would
        // be paid for and then thrown away.
        if (!settings.DryRun && !settings.AllowsPrompting)
        {
            return output.Fail(
                "A draft is saved only once you have reviewed it, so this needs a terminal. "
                + "Use --dry-run to see a draft without saving it.",
                ExitCode.InvalidArguments);
        }

        if (string.IsNullOrWhiteSpace(settings.About))
        {
            return output.Fail("Say what the guide should learn with --about.", ExitCode.InvalidArguments);
        }

        var drafted = await _drafter.DraftAsync(
            new VoiceGuideDraftRequest(settings.Id, settings.About, settings.Agent, settings.Model),
            cancellationToken).ConfigureAwait(false);

        if (drafted.Failed)
        {
            return output.Fail(drafted);
        }

        var draft = drafted.Value!;

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                dryRun = true,
                guide = draft.Guide,
                reference = draft.Guide.Reference,
                unreadable = draft.Unreadable,
                costUsd = draft.CostUsd,
            });

            return CommandOutput.Success();
        }
        else
        {
            VoiceGuideView.Write(output, draft.Guide, draft.Unreadable);
            output.WriteBlankLine();
            output.WriteLine($"[dim]Drafting cost {VoiceLearnCommand.Usd(draft.CostUsd)}.[/]");
        }

        if (settings.DryRun)
        {
            output.WriteLine("[dim]Nothing was saved.[/]");

            return CommandOutput.Success();
        }

        if (!_console.Confirm(
                $"Save this as {draft.Guide.Reference}? A saved version is never changed afterwards.", defaultValue: false))
        {
            output.WriteLine("Not saved.");

            return CommandOutput.Success();
        }

        var saved = await _store.SaveGuideAsync(draft.Guide, cancellationToken).ConfigureAwait(false);

        if (saved.Failed)
        {
            return output.Fail(saved);
        }

        output.WriteLine($"[green]+[/] Saved [cyan]{Markup.Escape(draft.Guide.Reference)}[/].");

        if (draft.Unreadable.Count > 0)
        {
            output.WriteLine(
                "[dim]It cannot be learned from until Loadout can read "
                + $"{Markup.Escape(string.Join(", ", draft.Unreadable))}.[/]");
        }

        return CommandOutput.Success();
    }
}

/// <summary>Options for listing guides.</summary>
public sealed class VoiceGuideListSettings : GlobalSettings
{
}

/// <summary>Lists the guides that ship and the ones drafted and saved here.</summary>
[Description("List voice guides: the ones that ship, and the ones drafted and saved in your workspace.")]
[CommandMeta(CommandCategory.AgentConfiguration, Intent = "list guides voice definitions versions")]
public sealed class VoiceGuideListCommand : AsyncCommand<VoiceGuideListSettings>
{
    private readonly VoiceStore _store;
    private readonly IAnsiConsole _console;

    public VoiceGuideListCommand(VoiceStore store, IAnsiConsole console)
    {
        _store = store;
        _console = console;
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        VoiceGuideListSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        var saved = await _store.SavedGuidesAsync(cancellationToken).ConfigureAwait(false);

        if (saved.Failed)
        {
            return output.Fail(saved);
        }

        var guides = VoiceGuides.All().Select(g => (Guide: g, Ships: true))
            .Concat(saved.Value!.Select(g => (Guide: g, Ships: false)))
            .ToList();

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                guides = guides.Select(g => new
                {
                    reference = g.Guide.Reference,
                    id = g.Guide.Id,
                    version = g.Guide.Version,
                    summary = g.Guide.Summary,
                    ships = g.Ships,
                    unreadable = VoiceGuideDrafting.Unreadable(g.Guide),
                }),
            });

            return CommandOutput.Success();
        }

        foreach (var (guide, ships) in guides)
        {
            var unreadable = VoiceGuideDrafting.Unreadable(guide);

            var state = ships
                ? "ships with Loadout"
                : unreadable.Count > 0
                    ? $"drafted; needs {string.Join(", ", unreadable)}, which Loadout cannot read yet"
                    : "drafted";

            output.WriteLine(
                $"[cyan]{Markup.Escape(guide.Reference)}[/]  {Markup.Escape(guide.Summary)}  [dim]{Markup.Escape(state)}[/]");
        }

        if (saved.Value!.Count == 0)
        {
            output.WriteBlankLine();
            output.WriteLine("[dim]Draft another with: loadout voice guide draft <id> --about \"what it should learn\"[/]");
        }

        return CommandOutput.Success();
    }
}

/// <summary>Options for showing a guide.</summary>
public sealed class VoiceGuideShowSettings : GlobalSettings
{
    [CommandArgument(0, "<GUIDE>")]
    [Description("The guide, as an id for its latest version or id/version for one in particular.")]
    public string Guide { get; init; } = string.Empty;
}

/// <summary>Shows one guide definition in full.</summary>
[Description("Show a guide definition: its sources, checks and every threshold a run is judged by.")]
[CommandMeta(CommandCategory.AgentConfiguration, Intent = "show guide definition thresholds sources checks")]
public sealed class VoiceGuideShowCommand : AsyncCommand<VoiceGuideShowSettings>
{
    private readonly VoiceStore _store;
    private readonly IAnsiConsole _console;

    public VoiceGuideShowCommand(VoiceStore store, IAnsiConsole console)
    {
        _store = store;
        _console = console;
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        VoiceGuideShowSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        var saved = await _store.SavedGuidesAsync(cancellationToken).ConfigureAwait(false);

        if (saved.Failed)
        {
            return output.Fail(saved);
        }

        var asked = settings.Guide.Trim().ToLowerInvariant();
        var parts = asked.Split('/', 2);
        int? version = parts.Length == 2 && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : null;

        if (parts.Length == 2 && version is null)
        {
            return output.Fail($"'{settings.Guide}' is not a guide. Name one as id or id/version.", ExitCode.InvalidArguments);
        }

        var found = VoiceGuides.All().Concat(saved.Value!)
            .Where(g => g.Id == parts[0] && (version is null || g.Version == version))
            .OrderByDescending(g => g.Version)
            .FirstOrDefault();

        if (found is null)
        {
            return output.Fail(
                $"There is no guide '{settings.Guide}'. Run 'loadout voice guide list' to see what there is.",
                ExitCode.InvalidArguments);
        }

        var unreadable = VoiceGuideDrafting.Unreadable(found);

        if (output.IsJson)
        {
            output.WriteJson(new { reference = found.Reference, guide = found, unreadable });

            return CommandOutput.Success();
        }

        VoiceGuideView.Write(output, found, unreadable);

        return CommandOutput.Success();
    }
}

/// <summary>A guide definition in words, for review and for showing.</summary>
internal static class VoiceGuideView
{
    public static void Write(CommandOutput output, VoiceGuide guide, IReadOnlyList<string> unreadable)
    {
        var e = guide.Evidence;
        var p = guide.Promotion;
        var c = guide.Cap;

        output.WriteLine($"[bold]{Markup.Escape(guide.Reference)}[/]  {Markup.Escape(guide.Summary)}");
        output.WriteBlankLine();
        output.WriteLine($"  Learns from    {Markup.Escape(string.Join(", ", guide.Sources))}");

        if (unreadable.Count > 0)
        {
            output.WriteLine(
                $"                [yellow]Loadout cannot read {Markup.Escape(string.Join(", ", unreadable))} yet.[/]");
        }

        output.WriteLine($"  Applies to     {Markup.Escape(string.Join(", ", guide.Surfaces))}");
        output.WriteLine(guide.CheckTypes.Count > 0
            ? $"  Checks         {Markup.Escape(string.Join(", ", guide.CheckTypes))}"
            : "  Checks         none, so every rule is judged and none can gate");
        output.WriteLine($"  Proposes a rule on {e.Citations} citation(s) from {e.Sources} source(s); short of that, it asks.");
        output.WriteLine(
            $"  Gates (from release 2) at {p.Agreement.ToString("P0", CultureInfo.InvariantCulture)} agreement "
            + $"on {p.ConsecutiveRuns} re-learn(s) in a row, with at least {p.EditFloor} of your own edits to score against.");
        output.WriteLine(
            $"  Stops gating after {p.DemotionFailures} failed re-learn(s). A rule short only because its marks lapsed "
            + $"counts as failing after {p.UnconfirmedLapses} re-learn(s) unconfirmed.");
        output.WriteLine(
            $"  Caps a run at its estimate plus {c.Margin.ToString("P0", CultureInfo.InvariantCulture)}, "
            + $"guessing {VoiceLearnCommand.Usd(c.SeedCallUsd)} a call at first, {c.BatchCharacters:N0} characters a call.");
        output.WriteLine(
            $"  Holds back {guide.Scoring.HoldOut.ToString("P0", CultureInfo.InvariantCulture)} of the writing to score rules against.");
    }
}
