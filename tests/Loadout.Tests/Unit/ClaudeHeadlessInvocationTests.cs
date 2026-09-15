using System.Globalization;
using FluentAssertions;
using Loadout.Agents;
using Loadout.Agents.Claude;
using Loadout.Core.Projects;
using Loadout.Models.Agents;
using Loadout.Models.Projects;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What a headless node is told on its command line.
/// </summary>
/// <remarks>
/// <para>
/// Every flag here was established by driving Claude Code 2.1.270 from a
/// process holding its pipes, not from documentation. Two of them exist
/// because of what happened without them: a node without
/// <c>--strict-mcp-config</c> connected every MCP server on this machine,
/// and a node without hooks disabled ran this machine's global hooks before
/// it had even reported starting.
/// </para>
/// <para>
/// The help text is a stub, so the tests say the same thing on a runner with
/// no Claude installed as on a workstation with one.
/// </para>
/// </remarks>
public sealed class ClaudeHeadlessInvocationTests
{
    /// <summary>Every marker the adapter looks for, in the shape 2.1.270 prints them.</summary>
    private const string FullHelp = """
        Usage: claude [options] [command] [prompt]
          --settings <file-or-json>           Path to a settings JSON file or a JSON string
          --model <model>                     Model for the current session
          --input-format <format>             Input format (only works with --print)
          --output-format <format>            Output format (only works with --print)
          --permission-mode <mode>            Permission mode to use for the session
          --permission-prompts <target>       Who answers permission prompts with
                                              --print: "host" (the SDK host or
                                              --permission-prompt-tool) or "none"
          --allowedTools, --allowed-tools <tools...>
          --disallowedTools, --disallowed-tools <tools...>
          --json-schema <schema>              JSON Schema for structured output
          --strict-mcp-config                 Only use MCP servers from --mcp-config
          -p, --print                         Print response and exit
        """;

    /// <summary>A build that can be launched for a person and not driven as a node.</summary>
    private const string InteractiveOnlyHelp = """
        Usage: claude [options] [command] [prompt]
          --settings <file-or-json>           Path to a settings JSON file or a JSON string
          --model <model>                     Model for the current session
          --permission-mode <mode>            Permission mode to use for the session
        """;

    [Fact]
    public async Task A_node_is_told_everything_explicitly()
    {
        var invocation = await BuildAsync(FullHelp, new HeadlessOptions(
            Permission: HeadlessPermission.AcceptEdits,
            AllowedTools: ["Read", "Bash(dotnet test:*)"],
            DeniedTools: ["Bash(git push:*)"],
            PermissionAnswerer: "mcp__loadout__permission",
            MaxTurns: 40,
            BudgetUsd: 4m,
            OutputSchemaJson: """{"type":"object"}"""));

        invocation.Warnings.Should().BeEmpty();

        invocation.Arguments.Should().ContainInOrder(
            "-p", "--verbose",
            "--input-format", "stream-json",
            "--output-format", "stream-json",
            "--max-turns", "40",
            "--max-budget-usd", "4",
            "--permission-mode", "acceptEdits",
            "--allowed-tools", "Read,Bash(dotnet test:*)",
            "--disallowed-tools", "Bash(git push:*)",
            "--permission-prompt-tool", "mcp__loadout__permission",
            "--json-schema", """{"type":"object"}""",
            "--strict-mcp-config",
            "--settings", """{"disableAllHooks":true}""");
    }

    [Theory]
    [InlineData(HeadlessPermission.Ask, "default")]
    [InlineData(HeadlessPermission.AcceptEdits, "acceptEdits")]
    [InlineData(HeadlessPermission.DenyUnlessAllowed, "dontAsk")]
    [InlineData(HeadlessPermission.Bypass, "bypassPermissions")]
    public async Task Each_permission_setting_has_one_spelling(HeadlessPermission permission, string spelling)
    {
        var invocation = await BuildAsync(FullHelp, new HeadlessOptions(Permission: permission));

        invocation.Arguments.Should().ContainInOrder("--permission-mode", spelling);
    }

    [Fact]
    public async Task The_budget_is_written_with_a_point_whatever_the_culture_says()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");

        try
        {
            var invocation = await BuildAsync(FullHelp, new HeadlessOptions(BudgetUsd: 0.25m));

            invocation.Arguments.Should().ContainInOrder("--max-budget-usd", "0.25");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task A_build_that_cannot_be_driven_says_so_and_stays_interactive()
    {
        var invocation = await BuildAsync(InteractiveOnlyHelp, new HeadlessOptions(MaxTurns: 5));

        invocation.Arguments.Should().NotContain("-p");
        invocation.Arguments.Should().NotContain("--max-turns");
        invocation.Warnings.Should().ContainSingle().Which.Should().Contain("cannot be driven without a terminal");
    }

    [Fact]
    public async Task An_interactive_launch_is_untouched()
    {
        var invocation = await BuildAsync(FullHelp, headless: null);

        invocation.Arguments.Should().NotContain("-p");
        invocation.Arguments.Should().NotContain("--strict-mcp-config");
        invocation.Arguments.Should().NotContain("--settings");
    }

    [Fact]
    public async Task Leaving_hooks_on_and_servers_shared_is_a_choice_the_options_can_make()
    {
        var invocation = await BuildAsync(FullHelp, new HeadlessOptions(DisableHooks: false, IsolateMcpServers: false));

        invocation.Arguments.Should().NotContain("--settings");
        invocation.Arguments.Should().NotContain("--strict-mcp-config");
    }

    private static async Task<AgentInvocation> BuildAsync(string help, HeadlessOptions? headless)
    {
        var adapter = new ClaudeAdapter(
            new StubResolver(Path.Combine(Path.GetTempPath(), "claude")),
            new StubProcessLauncher(help),
            []);

        var context = new AgentLaunchContext(
            new ProjectResolution(
                new ProjectRegistryEntry { Slug = "demo", Name = "Demo" },
                Path.GetTempPath(),
                null,
                0,
                false),
            Path.GetTempPath(),
            Path.GetTempPath(),
            null,
            [],
            Headless: headless);

        var result = await adapter.BuildInvocationAsync(context);

        result.Succeeded.Should().BeTrue(result.Error ?? "the invocation has to build");

        return result.Value!;
    }
}
