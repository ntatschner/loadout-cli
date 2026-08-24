using Loadout.Core.Mcp;
using Loadout.Tests.Fakes;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Covers reading the servers an agent already has.
/// <para>
/// The listing is written for a person, not for a program, so parsing it is a
/// poor contract — used only because it is the one place account connectors and
/// plugin servers appear at all. The fixture below is copied verbatim from a
/// real machine, including the health markers and the stdio server with an
/// absolute path in its command.
/// </para>
/// <para>
/// What matters most here is the failure behaviour: a line that does not fit
/// the shape is skipped rather than guessed at, so a change to the format costs
/// a warning and never a launch.
/// </para>
/// </summary>
public sealed class InstalledMcpParsingTests
{
    /// <summary>Real output, trimmed to the interesting rows.</summary>
    private const string RealListing = """
        Checking MCP server health…

        claude.ai n8n: https://n8n.tatux.in/mcp-server/http - ! Needs authentication
        claude.ai Postman: https://mcp.postman.com/minimal - ✔ Connected
        claude.ai Context7: https://mcp.context7.com/mcp - ✔ Connected
        serena: C:\Users\nrtat\.local\bin\uvx.exe --from git+https://github.com/oraios/serena serena start-mcp-server --context ide-assistant --project D:\git\Thecodesaiyan-web-app - ✔ Connected
        """;

    private static IReadOnlyList<McpEntry> Parse(string listing)
    {
        var reader = new InstalledMcpReader(
            new StubProcessLauncher(listing),
            new StubResolver("claude"),
            new FakeEnvironmentProvider(
                Path.Combine(Path.GetTempPath(), "loadout-mcp-" + Guid.NewGuid().ToString("N")),
                new Dictionary<string, string>()));

        return reader.ReadAsync("/nowhere").GetAwaiter().GetResult();
    }

    [Fact]
    public void Every_server_in_a_real_listing_is_found()
    {
        var entries = Parse(RealListing);

        entries.Select(e => e.Name)
            .Should().BeEquivalentTo(
                ["claude.ai n8n", "claude.ai Postman", "claude.ai Context7", "serena"]);
    }

    [Fact]
    public void The_health_marker_is_not_mistaken_for_part_of_the_target()
    {
        var entries = Parse(RealListing);

        var postman = entries.Single(e => e.Name == "claude.ai Postman");

        postman.Server.Url.Should().Be("https://mcp.postman.com/minimal");
        postman.Server.Url.Should().NotContain("Connected");
    }

    [Fact]
    public void A_command_containing_dashes_survives_the_status_being_cut_off()
    {
        // The serena line carries --from, --context and --project before the
        // status. Cutting at the first dash would have taken the command apart.
        var serena = Parse(RealListing).Single(e => e.Name == "serena");

        serena.Server.Command.Should().EndWith("uvx.exe");
        serena.Server.Args.Should().Contain("--project");
        serena.Server.Args.Should().NotContain(a => a.Contains("Connected", StringComparison.Ordinal));
    }

    [Fact]
    public void What_is_already_installed_is_marked_as_such()
    {
        // Not the workspace's to change, which is why it has a scope of its own.
        Parse(RealListing).Should().OnlyContain(e => e.Scope == McpScope.Installed);
    }

    [Fact]
    public void A_real_listing_reveals_the_clash_it_was_read_for()
    {
        var installed = Parse(RealListing);

        // A project declaring Context7 on top of the account connector is the
        // exact case this whole reader exists to catch.
        var proposed = installed
            .Concat([new McpEntry(
                "context7",
                McpScope.Project,
                new McpServer { Type = "http", Url = "https://mcp.context7.com/mcp" })])
            .ToList();

        McpService.Inspect(proposed)
            .Should().Contain(c => c.Kind == McpClashKind.DuplicateService);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Checking MCP server health…")]
    [InlineData("no colon here at all")]
    [InlineData(": leading colon")]
    public void A_line_that_is_not_a_server_is_skipped(string line) =>
        Parse(line).Should().BeEmpty();

    [Fact]
    public void An_agent_that_cannot_be_found_yields_nothing_rather_than_failing()
    {
        var reader = new InstalledMcpReader(
            new StubProcessLauncher(string.Empty),
            new StubResolver(null),
            new FakeEnvironmentProvider(
                Path.Combine(Path.GetTempPath(), "loadout-mcp-" + Guid.NewGuid().ToString("N")),
                new Dictionary<string, string>()));

        var act = () => reader.ReadAsync("/nowhere").GetAwaiter().GetResult();

        // A machine without the agent installed is a normal machine.
        act.Should().NotThrow();
        act().Should().BeEmpty();
    }

    /// <summary>Builds a home directory with a plugin that declares a server.</summary>
    private static string HomeWithPlugin(string pluginName, bool enabled)
    {
        var home = Path.Combine(Path.GetTempPath(), "loadout-plugin-" + Guid.NewGuid().ToString("N"));

        var pluginDirectory = Path.Combine(home, ".claude", "plugins", "cache", "owner", pluginName, "1.0.0");

        Directory.CreateDirectory(pluginDirectory);

        File.WriteAllText(
            Path.Combine(pluginDirectory, ".mcp.json"),
            """
            { "mcpServers": { "ctx": { "type": "http", "url": "https://mcp.context7.com/mcp" } } }
            """);

        File.WriteAllText(
            Path.Combine(home, ".claude", "settings.json"),
            $$"""
            { "enabledPlugins": { "{{pluginName}}@owner": {{(enabled ? "true" : "false")}} } }
            """);

        return home;
    }

    private static IReadOnlyList<McpEntry> FromHome(string home) =>
        new InstalledMcpReader(
                new StubProcessLauncher(string.Empty),
                new StubResolver(null),
                new FakeEnvironmentProvider(home, new Dictionary<string, string>()))
            .ReadAsync("/nowhere").GetAwaiter().GetResult();

    [Fact]
    public void A_server_from_an_enabled_plugin_is_found()
    {
        // Plugin servers appear in no listing and in no configuration file the
        // launcher can otherwise reach, yet a plugin reaching the same service
        // as an account connector loads every tool twice.
        var entries = FromHome(HomeWithPlugin("context7", enabled: true));

        entries.Should().ContainSingle()
            .Which.Server.Url.Should().Be("https://mcp.context7.com/mcp");
    }

    [Fact]
    public void A_server_from_a_disabled_plugin_is_ignored()
    {
        // A plugin that is installed and switched off contributes nothing, and
        // warning about its servers would describe something not happening.
        FromHome(HomeWithPlugin("context7", enabled: false)).Should().BeEmpty();
    }
}
