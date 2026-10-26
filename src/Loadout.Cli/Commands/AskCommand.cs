using System.ComponentModel;
using System.Globalization;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Ask;
using Loadout.Core.Tasks;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>
/// Asks Loadout something in plain words: what was spent, which tasks to pick
/// up, or what to start.
/// </summary>
/// <remarks>
/// <para>
/// Answered by Loadout's own code, never an agent. A request it cannot place
/// gets the closest requests it can, written out to ask as they stand.
/// </para>
/// <para>
/// A request that would start something is shown as a plan and goes no
/// further: here it is a preview by nature. Carrying a plan out needs the cap
/// settled beside it and somebody approving it, which is the palette's job.
/// </para>
/// </remarks>
[Description("Ask in plain words what was spent, which tasks to pick up, or what to start. Answered by Loadout itself; anything that would start something is shown as a plan.")]
[CommandMeta(CommandCategory.Workspace,
    Intent = "ask question how much spend cost which task easy needs work first queue start terminals plan natural language",
    Example = "loadout ask how much did I spend last tuesday")]
public sealed class AskCommand : AsyncCommand<AskCommand.Settings>
{
    private readonly IAskService _ask;
    private readonly IAnsiConsole _console;

    public AskCommand(IAskService ask, IAnsiConsole console)
    {
        _ask = ask;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "[REQUEST]")]
        [Description("What to ask, as words: how much did I spend last tuesday, list 5 easy open tasks.")]
        public string[] Words { get; init; } = [];

        public string Query => string.Join(' ', Words);

        public override ValidationResult Validate() =>
            Words.Length == 0
                ? ValidationResult.Error("Say what to ask: loadout ask how much did I spend this week")
                : base.Validate();
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        var result = await _ask.AskAsync(settings.Query, cancellationToken).ConfigureAwait(false);

        if (result.Failed)
        {
            return output.Fail(result);
        }

        var reply = result.Value!;

        if (output.IsJson)
        {
            output.WriteJson(Json(reply));

            return CommandOutput.Success();
        }

        output.WriteLine(Markup.Escape(reply.Summary));

        if (reply.Spend is { } spend)
        {
            foreach (var model in spend.Models)
            {
                output.WriteLine(
                    $"  [dim]{Markup.Escape(model.Model)}[/]  {model.Tokens:N0} tokens  "
                    + (model.Usd is { } usd ? Usd(usd) : "[dim]no price[/]"));
            }

            if (spend.Caveat is { } caveat)
            {
                output.WriteLine($"[yellow]{Markup.Escape(caveat)}[/]");
            }
        }

        foreach (var task in reply.Tasks)
        {
            WriteTask(output, task.Project, task.Task.Id, task.Task.Title, task.Reasons);
        }

        if (reply.Plan is { } plan)
        {
            output.WriteBlankLine();

            foreach (var step in plan.Steps)
            {
                output.WriteLine($"  [bold]•[/] {Markup.Escape(step.Says)}");

                if (step.Reasons.Count > 0)
                {
                    output.WriteLine($"    [dim]{Markup.Escape(string.Join("; ", step.Reasons))}[/]");
                }
            }

            if (plan.Capped)
            {
                output.WriteLine(plan.Cap.IsSet
                    ? $"  Cap: {(plan.Cap.Usd is { } cap ? Usd(cap) : "none, as team-budget says")}"
                    : "  Cap: [yellow]not set[/] — give one before approving, or set a default with 'loadout config set team-budget <usd>'.");
            }

            foreach (var left in plan.Left)
            {
                output.WriteLine($"  [dim]Left out: {Markup.Escape(left)}[/]");
            }

            if (plan.TeamChoices.Count > 0)
            {
                output.WriteLine($"  [dim]Teams to choose from: {Markup.Escape(string.Join(", ", plan.TeamChoices))}[/]");
            }

            output.WriteLine("[dim]Nothing has been started: a plan here is a preview.[/]");
        }

        foreach (var near in reply.NearMisses)
        {
            output.WriteLine($"  [dim]try[/] loadout ask {Markup.Escape(near.Rewrite)}");
        }

        return CommandOutput.Success();
    }

    private static void WriteTask(CommandOutput output, string project, string id, string title, IReadOnlyList<string> reasons)
    {
        var where = project.Length == 0 ? "workspace" : project;

        output.WriteLine($"  [bold]{Markup.Escape(id)}[/] [dim]({Markup.Escape(where)})[/] {Markup.Escape(title)}");

        if (reasons.Count > 0)
        {
            output.WriteLine($"    [dim]{Markup.Escape(string.Join("; ", reasons))}[/]");
        }
    }

    private static string Usd(decimal usd) => usd.ToString("$#,##0.00", CultureInfo.InvariantCulture);

    /// <summary>The reply as a script, Studio or the daemon would read it.</summary>
    internal static object Json(AskReply reply) => new
    {
        query = reply.Query,
        kind = reply.Kind.ToString().ToLowerInvariant(),
        answeredBy = reply.AnsweredBy,
        summary = reply.Summary,
        request = reply.Request is { } request
            ? new
            {
                shape = Shape(request.Shape),
                period = request.Period is { } period
                    ? new { from = period.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), until = period.Until.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), label = period.Label }
                    : null,
                project = request.Project,
                count = request.Count,
                pick = Pick(request.Pick),
                team = request.Team,
                start = request.Start,
            }
            : null,
        spend = reply.Spend is { } spend
            ? new
            {
                estimate = true,
                basis = "list rates",
                pricesCheckedOn = Loadout.Core.Usage.PriceTable.CheckedOn,
                usd = spend.Estimate.Usd,
                unpricedTokens = spend.Estimate.UnpricedTokens,
                unpricedModels = spend.Estimate.UnpricedModels,
                models = spend.Models.Select(model => new { model = model.Model, tokens = model.Tokens, usd = model.Usd }),
                caveat = spend.Caveat,
            }
            : null,
        tasks = reply.Tasks.Select(Task),
        plan = reply.Plan is { } plan
            ? new
            {
                shape = Shape(plan.Shape),
                team = plan.Team,
                teamChoices = plan.TeamChoices,
                steps = plan.Steps.Select(step => new
                {
                    kind = step.Kind switch
                    {
                        AskStepKind.QueueTask => "queue-task",
                        AskStepKind.StartTeam => "start-team",
                        _ => "open-terminal",
                    },
                    project = step.Project,
                    task = step.Task,
                    says = step.Says,
                    reasons = step.Reasons,
                }),
                capped = plan.Capped,
                cap = plan.Cap.Usd,
                capNone = plan.Cap.None,
                left = plan.Left,
                needs = plan.Needs,
            }
            : null,
        nearMisses = reply.NearMisses.Select(near => new { shape = Shape(near.Shape), rewrite = near.Rewrite }),
    };

    private static object Task(RankedTask ranked) => new
    {
        project = ranked.Project,
        id = ranked.Task.Id,
        title = ranked.Task.Title,
        score = ranked.Score,
        reasons = ranked.Reasons,
    };

    private static string Shape(AskShape shape) => shape switch
    {
        AskShape.Spend => "spend",
        AskShape.Tasks => "tasks",
        AskShape.TeamQueue => "team-queue",
        _ => "terminals",
    };

    private static string Pick(TaskPick pick) => pick switch
    {
        TaskPick.Easy => "easy",
        TaskPick.NeedsWorkFirst => "needs-work-first",
        _ => "any",
    };
}
