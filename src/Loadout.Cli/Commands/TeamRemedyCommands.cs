using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Configuration;
using Loadout.Core.Teams;
using Loadout.Models;
using Loadout.Models.Teams;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>Settings every command about a team's remedies takes.</summary>
public class RemedySettings : GlobalSettings
{
    [CommandOption("--team <TEAM>")]
    [Description("The team whose directory to look in, as 'team list' names it.")]
    public string Team { get; init; } = string.Empty;
}

/// <summary>
/// What a team has worked out how to fix, and whether it may do it again on
/// its own.
/// </summary>
/// <remarks>
/// <para>
/// A team that investigates a problem and fixes it has learned something, and
/// a team whose declarations tell it to write that down accumulates a shelf of
/// scripts. Letting a remediator run one of them unattended is a decision
/// somebody has to make deliberately, about a particular script, on this
/// machine.
/// </para>
/// <para>
/// Two keys, neither of them an agent's: a person trusts the script, and this
/// machine's configuration says what that kind of task may do. Every command
/// here is one half or the other.
/// </para>
/// </remarks>
[Description("List what a team has worked out how to fix, and whether it is trusted here.")]
[CommandMeta(CommandCategory.Start,
    Intent = "team remedies remediation scripts fixes trusted automation")]
public sealed class TeamRemediesCommand : AsyncCommand<RemedySettings>
{
    private readonly IRemedyBook _book;
    private readonly IConfigurationService _configuration;
    private readonly IAnsiConsole _console;

    public TeamRemediesCommand(IRemedyBook book, IConfigurationService configuration, IAnsiConsole console)
    {
        _book = book;
        _configuration = configuration;
        _console = console;
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        RemedySettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        if (settings.Team is not { Length: > 0 })
        {
            return output.Fail(
                "Which team? Name one with --team, as 'loadout team list' shows it.",
                ExitCode.InvalidArguments);
        }

        var read = _book.All(settings.Team);

        if (read.Failed)
        {
            return output.Fail(read);
        }

        var machine = await _configuration.LoadMachineAsync(cancellationToken).ConfigureAwait(false);
        var rules = machine.Value?.Teams.Remediation ?? [];
        var trusted = machine.Value?.Teams.TrustedRemedies;

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                team = settings.Team,
                directory = _book.DirectoryOf(settings.Team),
                remedies = read.Value!.Select(one => new
                {
                    one.Name,
                    one.Kind,
                    one.What,
                    trusted = RemedyCeiling.For(trusted, settings.Team).Any(agreed =>
                        string.Equals(agreed.Remedy, one.Name, StringComparison.OrdinalIgnoreCase)),
                    claimsTrust = one.ClaimsTrust,
                    rule = Rule(rules, one.Kind),
                    ruling = Ruling(_book, settings.Team, one, rules, trusted).Ruling.ToString().ToLowerInvariant(),
                    one.Revision,
                }),
            });

            return CommandOutput.Success();
        }

        output.WriteLine($"[bold]{Markup.Escape(settings.Team)}[/]  [dim]{Markup.Escape(_book.DirectoryOf(settings.Team))}[/]");

        if (read.Value!.Count == 0)
        {
            output.WriteBlankLine();
            output.WriteLine(
                "  [dim]Nothing registered yet. A team whose declarations tell it to write its "
                + "fixes down puts them here.[/]");

            return CommandOutput.Success();
        }

        output.WriteBlankLine();

        foreach (var remedy in read.Value!)
        {
            var decided = Ruling(_book, settings.Team, remedy, rules, trusted);

            output.WriteLine(
                $"  {Markup.Escape(remedy.Name),-28} {Markup.Escape(Kind(remedy)),-14} "
                + Says(decided.Ruling)
                + (remedy.Revision > 0 ? $"  [dim]rev {remedy.Revision}[/]" : string.Empty));

            output.WriteLine($"  {string.Empty,-28} [dim]{Markup.Escape(decided.Because)}[/]");
        }

        return CommandOutput.Success();
    }

    /// <summary>What this machine says about a kind, or what it says about none.</summary>
    internal static string Rule(IReadOnlyDictionary<string, string> rules, string kind) =>
        rules.TryGetValue(Kind(kind), out var said) ? said : RemedyRules.Default;

    /// <summary>The ruling for one remedy, with its script compared.</summary>
    internal static RemedyCeiling.Decision Ruling(
        IRemedyBook book,
        string team,
        Remedy remedy,
        IReadOnlyDictionary<string, string> rules,
        IReadOnlyList<Loadout.Models.Configuration.TrustedRemedy>? trusted)
    {
        var script = book.ScriptOf(team, remedy);

        return RemedyCeiling.Decide(
            remedy,
            Rule(rules, remedy.Kind),
            script.Succeeded ? script.Value : null,
            RemedyCeiling.For(trusted, team));
    }

    /// <summary>The word for a ruling, padded then coloured - never the other way about.</summary>
    internal static string Says(RemedyRuling ruling)
    {
        var word = (ruling switch
        {
            RemedyRuling.Run => "runs unasked",
            RemedyRuling.Refuse => "refused",
            _ => "asks first",
        }).PadRight(14);

        return ruling switch
        {
            RemedyRuling.Run => $"[green]{word}[/]",
            RemedyRuling.Refuse => $"[red]{word}[/]",
            _ => $"[yellow]{word}[/]",
        };
    }

    internal static string Kind(Remedy remedy) => Kind(remedy.Kind);

    internal static string Kind(string kind) =>
        kind is { Length: > 0 } named ? named.Trim() : "unclassified";
}

/// <summary>One remedy in full, which is what somebody deciding needs.</summary>
[Description("Show one remedy: what it does, what it assumes, and whether it is trusted here.")]
[CommandMeta(CommandCategory.Start, Intent = "team remedy show detail script trusted")]
public sealed class TeamRemedyShowCommand : AsyncCommand<TeamRemedyShowCommand.Settings>
{
    private readonly IRemedyBook _book;
    private readonly IConfigurationService _configuration;
    private readonly IAnsiConsole _console;

    public TeamRemedyShowCommand(IRemedyBook book, IConfigurationService configuration, IAnsiConsole console)
    {
        _book = book;
        _configuration = configuration;
        _console = console;
    }

    public sealed class Settings : RemedySettings
    {
        [CommandArgument(0, "<REMEDY>")]
        [Description("The remedy, as 'team remedies' names it.")]
        public string Remedy { get; init; } = string.Empty;

        [CommandOption("--script")]
        [Description("Print the script itself as well.")]
        public bool Script { get; init; }
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        if (settings.Team is not { Length: > 0 })
        {
            return output.Fail("Which team? Name one with --team.", ExitCode.InvalidArguments);
        }

        var found = _book.Find(settings.Team, settings.Remedy);

        if (found.Failed)
        {
            return output.Fail(found);
        }

        var remedy = found.Value!;
        var machine = await _configuration.LoadMachineAsync(cancellationToken).ConfigureAwait(false);
        var rules = machine.Value?.Teams.Remediation ?? [];
        var trusted = machine.Value?.Teams.TrustedRemedies;
        var agreed = RemedyCeiling.For(trusted, settings.Team).FirstOrDefault(one =>
            string.Equals(one.Remedy, remedy.Name, StringComparison.OrdinalIgnoreCase));

        var decided = TeamRemediesCommand.Ruling(_book, settings.Team, remedy, rules, trusted);
        var script = _book.ScriptOf(settings.Team, remedy);

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                remedy.Name,
                remedy.Kind,
                remedy.What,
                remedy.Assumes,
                remedy.Proves,
                remedy.Script,
                remedy.RegisteredBy,
                remedy.RegisteredAt,
                remedy.Revision,
                trusted = agreed is not null,
                trustedBy = agreed?.By,
                trustedAt = agreed?.At,
                claimsTrust = remedy.ClaimsTrust,
                rule = TeamRemediesCommand.Rule(rules, remedy.Kind),
                ruling = decided.Ruling.ToString().ToLowerInvariant(),
                because = decided.Because,
                script = settings.Script && script.Succeeded ? script.Value : null,
            });

            return CommandOutput.Success();
        }

        output.WriteLine($"[bold]{Markup.Escape(remedy.Name)}[/]  [dim]{Markup.Escape(TeamRemediesCommand.Kind(remedy))}[/]");

        if (remedy.What is { Length: > 0 })
        {
            output.WriteLine($"  {Markup.Escape(remedy.What)}");
        }

        output.WriteBlankLine();

        Line(output, "assumes", remedy.Assumes);
        Line(output, "proves", remedy.Proves);
        Line(output, "script", remedy.Script);
        Line(output, "from", remedy.RegisteredBy);
        Line(output, "revision", remedy.Revision > 0 ? remedy.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty);

        output.WriteBlankLine();
        output.WriteLine(
            $"  trust      {(agreed is not null ? "[green]agreed here[/]" : "[yellow]not agreed here[/]")}"
            + (agreed?.By is { Length: > 0 } who ? $" [dim]by {Markup.Escape(who)}[/]" : string.Empty));

        // A record claiming what this machine never agreed to is worth showing
        // rather than quietly ignoring. It is either a file somebody copied in
        // or a node having a go, and both are things to look at.
        if (remedy.ClaimsTrust && agreed is null)
        {
            output.WriteLine(
                "  [red]its own record claims to be trusted, which decides nothing[/]");
        }

        output.WriteLine($"  this kind  {Markup.Escape(TeamRemediesCommand.Rule(rules, remedy.Kind))}");
        output.WriteLine($"  so         {TeamRemediesCommand.Says(decided.Ruling)}");
        output.WriteLine($"  [dim]{Markup.Escape(decided.Because)}[/]");

        if (script.Failed)
        {
            output.WriteBlankLine();
            output.WriteLine($"  [red]{Loadout.Tui.Shown.Safely(script.Error!)}[/]");
        }
        else if (settings.Script)
        {
            // Redacted, not merely escaped. Escaping stops a bracket being read
            // as markup and says nothing about what the text contains, and this
            // is an arbitrary file somebody's agent wrote - a script that
            // carries a credential is exactly the thing not to print.
            output.WriteBlankLine();
            output.WriteLine(Loadout.Tui.Shown.Safely(script.Value!));
        }

        return CommandOutput.Success();
    }

    private static void Line(CommandOutput output, string name, string value)
    {
        if (value is { Length: > 0 })
        {
            output.WriteLine($"  {name,-10} {Markup.Escape(value)}");
        }
    }
}
