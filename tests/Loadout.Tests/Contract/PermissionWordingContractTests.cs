using System.Text.Json;
using FluentAssertions;
using Loadout.Core.Teams;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// A node's permission question, asked through the real MCP tool, written with
/// the words every surface shows.
/// </summary>
/// <remarks>
/// The plain line, the refused part and the warnings are worked out by the
/// answerer, because it is the last place the call's whole input is to hand.
/// So the only proof they reach a screen is the answerer writing them: started
/// as an agent starts it, asked as an agent asks, and the question it leaves
/// for a person read back off the disk.
/// </remarks>
[Collection(ContractCollection.Name)]
public sealed class PermissionWordingContractTests
{
    [BuiltCliFact]
    public async Task The_question_left_for_a_person_carries_the_plain_line_the_refused_part_and_the_warning()
    {
        using var loadout = new LoadoutProcess();
        var run = Path.Combine(loadout.Home, "run");
        var policy = await NodePermissions.WriteAsync(
            run,
            new NodePolicy("20261026-1200-abcd", "implementer", "builder", ["Bash(ls:*)"], [], Ask: true));

        using var session = await McpServerContractTests.StartAsync(loadout.Talking("mcp", "serve", "--policy", policy));

        // The call blocks until somebody answers, so it is sent and left
        // running while the question is read and answered.
        var calling = session.RequestAsync("tools/call", new
        {
            name = "loadout_permission",
            arguments = new
            {
                tool_name = "Bash",
                input = new { command = "ls build && rm -rf build", description = "List the build output" },
                tool_use_id = "toolu_1",
            },
        });

        PendingAsk? asked = null;

        for (var tries = 0; tries < 200 && asked is null; tries++)
        {
            asked = NodePermissions.Pending(run).FirstOrDefault();

            if (asked is null)
            {
                await Task.Delay(100);
            }
        }

        asked.Should().NotBeNull("the question has to be put where a person will find it");

        asked!.Said.Should().Be("list the build output");
        asked.Blocked.Should().Be("rm -rf build");
        asked.Flags.Should().Equal(AskWording.Deletes);

        // Unchanged: the raw command, which is what a don't-ask-again rule is
        // matched against.
        asked.Target.Should().Be("ls build && rm -rf build");

        await NodePermissions.AnswerAsync(run, asked.Id, new AskAnswer(false, NodePermissions.Told(false, null), "no"));

        var answer = await calling;
        var text = answer.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;

        JsonDocument.Parse(text).RootElement.GetProperty("behavior").GetString().Should().Be("deny");
    }
}
