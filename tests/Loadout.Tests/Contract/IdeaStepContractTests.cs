using FluentAssertions;
using Loadout.Core.Ideas;
using Loadout.Models.Ideas;
using Loadout.Tui.Terminal;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// Every command line the ideas screen builds, put through the real parser,
/// including prose that starts with a dash, which a parser reads as an option.
/// </summary>
[Collection(ContractCollection.Name)]
public sealed class IdeaStepContractTests
{
    public static TheoryData<string, string[]> Steps()
    {
        var global = new IdeaPlace(null, "status-page");
        var project = new IdeaPlace("website", "status-page");

        IdeaStep[] steps =
        [
            IdeaSteps.Add("- a bullet copied from a list", "website"),
            IdeaSteps.Add("plain", null),
            IdeaSteps.Refine(global),
            IdeaSteps.Answer(project, "Q1", "private, on the LAN"),
            IdeaSteps.Answer(global, "Q2", "- only on the LAN"),
            IdeaSteps.Choose(global, "L1", "L1b"),
            IdeaSteps.Judge(global, IdeaVerdict.Keep, ["L1", "A2"]),
            IdeaSteps.Judge(project, IdeaVerdict.Drop, ["A1"]),
            IdeaSteps.Improve(global, "plan", "make it smaller"),
            IdeaSteps.Improve(global, "L2", "- no JavaScript"),
            IdeaSteps.Accept(global, new IdeaTarget("website", null)),
            IdeaSteps.Accept(project, new IdeaTarget(null, "Lab watch")),
            IdeaSteps.Remove(project),
        ];

        var data = new TheoryData<string, string[]>();

        foreach (var step in steps)
        {
            data.Add(step.Command, [.. step.Arguments]);
        }

        return data;
    }

    [BuiltCliTheory]
    [MemberData(nameof(Steps))]
    public async Task Every_step_the_ideas_screen_builds_is_a_line_the_parser_accepts(string command, string[] arguments)
    {
        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync([.. command.Split(' '), .. arguments, "--non-interactive", "--dry-run"]);

        var everything = run.StandardOutput + run.StandardError;

        everything.Should().NotContain("Unknown option", string.Join(' ', arguments));
        everything.Should().NotContain("Unexpected option", string.Join(' ', arguments));
        everything.Should().NotContain("Unknown command");
        everything.Should().NotContain("Could not match", "an argument the parser could not place is a refused line");
        everything.Should().NotContain("Option does not have a name", "a value starting with a dash was read as an option");
        everything.Should().NotContain("missing required argument");
    }
}
