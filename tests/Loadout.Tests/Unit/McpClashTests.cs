using Loadout.Core.Mcp;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Covers what is wrong with a set of MCP servers.
/// <para>
/// Claude reads servers from an account's connectors, from installed plugins,
/// from a project file and from a user file, and nothing reconciles them. Every
/// case below was taken from a real machine, where Context7 was reachable twice
/// under two names and a stdio server had an absolute path baked into its
/// command. Neither is visible until something behaves oddly.
/// </para>
/// </summary>
public sealed class McpClashTests
{
    private static McpEntry Http(string name, string url, McpScope scope = McpScope.Project) =>
        new(name, scope, new McpServer { Type = "http", Url = url });

    private static McpEntry Stdio(string name, string command, params string[] args) =>
        new(name, McpScope.Project, new McpServer { Command = command, Args = [.. args] });

    [Fact]
    public void A_tidy_set_reports_nothing()
    {
        var clashes = McpService.Inspect(
        [
            Http("context7", "https://mcp.context7.com/mcp"),
            Http("sentry", "https://mcp.sentry.dev/mcp"),
            Stdio("serena", "uvx", "--from", "git+https://github.com/oraios/serena"),
        ]);

        clashes.Should().BeEmpty();
    }

    [Fact]
    public void One_service_under_two_names_is_reported()
    {
        // The live case: an account connector and a plugin reaching the same
        // upstream. Both sets of tools load and the model sees each twice.
        var clashes = McpService.Inspect(
        [
            Http("claude_ai_Context7", "https://mcp.context7.com/mcp"),
            Http("plugin_context7", "https://mcp.context7.com/mcp"),
        ]);

        clashes.Should().ContainSingle()
            .Which.Kind.Should().Be(McpClashKind.DuplicateService);
    }

    [Theory]
    // The same endpoint written the several ways people write one. Reporting
    // these as different servers would make the check useless on exactly the
    // cases it exists for.
    [InlineData("https://mcp.context7.com/mcp", "https://mcp.context7.com/mcp/")]
    [InlineData("https://mcp.context7.com/mcp", "https://MCP.Context7.com/mcp")]
    public void Endpoints_that_differ_only_cosmetically_are_the_same_service(string a, string b)
    {
        var clashes = McpService.Inspect([Http("one", a), Http("two", b)]);

        clashes.Should().Contain(c => c.Kind == McpClashKind.DuplicateService);
    }

    [Fact]
    public void Genuinely_different_services_are_left_alone()
    {
        var clashes = McpService.Inspect(
        [
            Http("context7", "https://mcp.context7.com/mcp"),
            Http("postman", "https://mcp.postman.com/minimal"),
        ]);

        clashes.Should().NotContain(c => c.Kind == McpClashKind.DuplicateService);
    }

    [Fact]
    public void The_same_name_in_two_scopes_is_reported_as_shadowed()
    {
        var clashes = McpService.Inspect(
        [
            Http("serena", "https://example.invalid/a", McpScope.Global),
            Http("serena", "https://example.invalid/b", McpScope.Project),
        ]);

        // The project one wins, which is usually intended — but silently, and
        // the other simply never loads.
        clashes.Should().Contain(c => c.Kind == McpClashKind.ShadowedName);
    }

    [Theory]
    // A workspace is shared between machines by design, so a path that is right
    // on one is wrong on the rest.
    [InlineData("D:\\git\\Thecodesaiyan-web-app")]
    [InlineData("/home/n/code/alpha")]
    [InlineData("/Users/n/code/alpha")]
    public void An_absolute_path_in_an_argument_is_reported(string path)
    {
        var clashes = McpService.Inspect([Stdio("serena", "uvx", "--project", path)]);

        clashes.Should().ContainSingle()
            .Which.Kind.Should().Be(McpClashKind.MachineSpecificPath);
    }

    [Fact]
    public void An_absolute_command_is_reported_too()
    {
        var clashes = McpService.Inspect(
            [Stdio("serena", "C:\\Users\\n\\.local\\bin\\uvx.exe", "start")]);

        clashes.Should().ContainSingle()
            .Which.Kind.Should().Be(McpClashKind.MachineSpecificPath);
    }

    [Fact]
    public void A_command_found_on_the_path_is_not_machine_specific()
    {
        // The whole point of naming a command rather than a path: it resolves
        // wherever it is installed.
        var clashes = McpService.Inspect([Stdio("serena", "uvx", "--from", "git+https://x/y")]);

        clashes.Should().BeEmpty();
    }

    [Fact]
    public void A_relative_argument_is_not_machine_specific()
    {
        var clashes = McpService.Inspect([Stdio("tool", "npx", "./scripts/serve.js")]);

        clashes.Should().BeEmpty();
    }

    [Fact]
    public void Every_clash_names_the_servers_it_is_about()
    {
        var clashes = McpService.Inspect(
        [
            Http("a", "https://same.invalid/mcp"),
            Http("b", "https://same.invalid/mcp"),
        ]);

        // A warning nobody can act on is only noise; the names are what makes
        // it actionable.
        clashes.Should().OnlyContain(c => c.Names.Count > 0);
        clashes[0].Names.Should().Contain(["a", "b"]);
    }

    [Fact]
    public void A_server_with_nothing_to_reach_is_not_matched_against_others()
    {
        // Two half-written entries are not "the same service"; they are two
        // mistakes, and pairing them would bury the real ones.
        var clashes = McpService.Inspect(
        [
            new McpEntry("one", McpScope.Project, new McpServer()),
            new McpEntry("two", McpScope.Project, new McpServer()),
        ]);

        clashes.Should().NotContain(c => c.Kind == McpClashKind.DuplicateService);
    }
}
