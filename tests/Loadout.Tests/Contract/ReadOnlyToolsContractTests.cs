using System.Reflection;
using FluentAssertions;
using Loadout.Cli.Commands;
using ModelContextProtocol.Server;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// The tools the Ask agent is given: those that read, and nothing that can
/// record, declare, approve or change anything.
/// </summary>
[Collection(ContractCollection.Name)]
public sealed class ReadOnlyToolsContractTests
{
    private static IReadOnlyList<string> AllTools() =>
        typeof(LoadoutTools).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(method => method.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .OfType<string>()
            .ToList();

    [Fact]
    public void Every_tool_is_said_to_read_or_to_act_and_never_both()
    {
        // So a new tool is not handed to the read-only agent until somebody
        // has decided it only reads.
        foreach (var tool in AllTools())
        {
            (LoadoutTools.ReadOnly.Contains(tool) ^ LoadoutTools.Acting.Contains(tool))
                .Should().BeTrue($"'{tool}' has to be in exactly one of LoadoutTools.ReadOnly and LoadoutTools.Acting");
        }

        LoadoutTools.ReadOnly.Concat(LoadoutTools.Acting).Should().BeSubsetOf(AllTools(), "a name in either list is a tool that exists");
    }

    [BuiltCliFact]
    public async Task Served_read_only_it_offers_the_reading_tools_and_nothing_else()
    {
        using var loadout = new LoadoutProcess();
        using var session = await McpServerContractTests.StartAsync(loadout.Talking("mcp", "serve", "--project", "nowhere", "--read-only"));

        var tools = (await session.RequestAsync("tools/list"))
            .GetProperty("result").GetProperty("tools")
            .EnumerateArray()
            .Select(tool => tool.GetProperty("name").GetString()!)
            .ToList();

        tools.Should().BeEquivalentTo(LoadoutTools.ReadOnly);
        tools.Should().NotContain(["loadout_remember", "loadout_task_declare", "loadout_permission", "loadout_task_apply"]);
    }
}
