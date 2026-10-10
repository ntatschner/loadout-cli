using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Core.Teams.Daemon;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// Every command line the dashboard's tasks page can cause, put through the
/// real parser, with prose that starts with a dash in every box that takes prose.
/// </summary>
[Collection(ContractCollection.Name)]
public sealed class DashboardTaskContractTests
{
    private static readonly string[] Refusals =
    [
        "Unknown option", "Unexpected option", "Unknown command",
        "Option does not have a name", "missing required argument", "Could not match",
    ];

    public static TheoryData<string> Verbs()
    {
        var data = new TheoryData<string>();

        foreach (var verb in DashboardTasks.Verbs)
        {
            data.Add(verb);
        }

        return data;
    }

    [BuiltCliTheory]
    [MemberData(nameof(Verbs))]
    public async Task Every_verb_the_tasks_page_sends_is_a_line_the_parser_accepts(string verb)
    {
        var (command, arguments) = DashboardTasks.Maps(new TaskAction(
            verb, Id: "fix-login", Project: "website", Title: "- Fix the login", Note: "- a bullet", State: "blocked"));

        await AcceptedAsync(verb, [.. command.Split(' '), .. arguments, "--dry-run"]);
    }

    [BuiltCliFact]
    public async Task A_run_on_a_task_is_a_line_the_parser_accepts()
    {
        var typed = DashboardActions.Starting(DashboardTasks.Running(
            new Loadout.Models.Tasks.TaskItem { Id = "fix-login", Title = "- Fix the login", Note = "- a bullet" },
            "website",
            "iterating-project"));

        await AcceptedAsync("run", ["team", "run", .. typed, "--dry-run"]);
    }

    [BuiltCliFact]
    public async Task A_run_leaving_out_the_project_s_criteria_is_a_line_the_parser_accepts()
    {
        var typed = DashboardActions.Starting(new StartRequest(
            "iterating-project", "- Fix the login", "website",
            DropProjectDoneWhen: ["- docs are in the house voice", "the suite passes"]));

        await AcceptedAsync("run", ["team", "run", .. typed, "--dry-run"]);
        await AcceptedAsync("run", ["team", "run", "iterating-project", "Fix the login", "--no-project-done-when", "--dry-run"]);
    }

    private static async Task AcceptedAsync(string verb, string[] line)
    {
        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync(line);
        var everything = run.StandardOutput + run.StandardError;

        foreach (var refusal in Refusals)
        {
            everything.Should().NotContain(refusal, $"'{verb}' types '{string.Join(' ', line)}'");
        }
    }
}
