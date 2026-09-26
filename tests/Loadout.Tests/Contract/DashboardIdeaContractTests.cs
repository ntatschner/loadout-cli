using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Core.Teams.Daemon;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// Every command line the dashboard's ideas page can cause, put through the
/// real parser, with prose that starts with a dash in every box that takes prose.
/// </summary>
[Collection(ContractCollection.Name)]
public sealed class DashboardIdeaContractTests
{
    public static TheoryData<string> Verbs()
    {
        var data = new TheoryData<string>();

        foreach (var verb in DashboardIdeas.Verbs)
        {
            data.Add(verb);
        }

        return data;
    }

    [BuiltCliTheory]
    [MemberData(nameof(Verbs))]
    public async Task Every_verb_the_ideas_page_sends_is_a_line_the_parser_accepts(string verb)
    {
        var (command, arguments) = DashboardIdeas.Maps(new IdeaAction(
            verb,
            Id: "status-page",
            Project: "website",
            Text: "- a bullet",
            Question: "Q1",
            Answer: "- only on the LAN",
            Layer: "L1",
            Option: "L1b",
            Pieces: ["L1"],
            Piece: "plan",
            Request: "- smaller",
            To: verb == "accept" ? null : "homelab",
            NewProject: verb == "accept" ? "- Lab watch" : null,
            Only: [1]));

        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync([.. command.Split(' '), .. arguments, "--dry-run"]);
        var everything = run.StandardOutput + run.StandardError;

        foreach (var refusal in new[]
        {
            "Unknown option", "Unexpected option", "Unknown command",
            "Option does not have a name", "missing required argument", "Could not match",
        })
        {
            everything.Should().NotContain(refusal, $"'{verb}' types '{command} {string.Join(' ', arguments)}'");
        }
    }
}
