using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// Pins the shape of every <c>--json</c> document the command line produces.
/// <para>
/// <c>--json</c> is a public contract: scripts read these documents, and a
/// property renamed during an unrelated refactor breaks every one of them
/// silently. Nothing pinned any of these shapes before, so the contract existed
/// only as an intention.
/// </para>
/// <para>
/// Key sets and types are asserted, never formatting or values. Adding a
/// property is allowed and must stay allowed; removing or renaming one is the
/// breakage worth catching, and so is a document that changes from an object to
/// an array underneath somebody's parser.
/// </para>
/// <para>
/// These run the built command line as a process against a throwaway home,
/// because that is what a script actually receives. Calling into the command
/// types would test neither the writer nor the exit code.
/// </para>
/// </summary>
[Collection(ContractCollection.Name)]
public sealed class JsonContractTests
{
    /// <summary>Asserts the document is an object carrying at least these properties.</summary>
    private static void ShouldHave(JsonElement document, params string[] properties)
    {
        document.ValueKind.Should().Be(
            JsonValueKind.Object,
            "a script parsing this expects an object");

        var present = document.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var property in properties)
        {
            present.Should().Contain(
                property,
                $"'{property}' is part of the published shape and removing it breaks callers");
        }
    }

    [BuiltCliFact]
    public async Task Doctor_reports_a_verdict_and_its_checks()
    {
        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync("doctor", "--json");
        var json = run.Json();

        ShouldHave(json, "verdict", "overall", "checks", "remedies");

        json.GetProperty("checks").ValueKind.Should().Be(JsonValueKind.Array);
        json.GetProperty("remedies").ValueKind.Should().Be(JsonValueKind.Array);

        // Every check carries the fields a caller filters and displays on.
        foreach (var check in json.GetProperty("checks").EnumerateArray())
        {
            ShouldHave(check, "category", "name", "severity", "detail", "fixable");

            check.GetProperty("fixable").ValueKind
                .Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
        }
    }

    [BuiltCliFact]
    public async Task Status_reports_the_workspace_agents_and_projects()
    {
        using var loadout = new LoadoutProcess();

        ShouldHave(
            (await loadout.RunAsync("status", "--json")).Json(),
            "workspace",
            "agents",
            "projects",
            "defaultAgent");
    }

    [BuiltCliFact]
    public async Task Project_list_is_an_object_wrapping_the_projects()
    {
        using var loadout = new LoadoutProcess();

        var json = (await loadout.RunAsync("project", "list", "--json")).Json();

        // Wrapped rather than a bare array, deliberately: it leaves room to add
        // sibling properties without changing what callers already index.
        ShouldHave(json, "projects");
        json.GetProperty("projects").ValueKind.Should().Be(JsonValueKind.Array);
    }

    [BuiltCliFact]
    public async Task Config_list_reports_every_setting_with_its_scope()
    {
        using var loadout = new LoadoutProcess();

        var json = (await loadout.RunAsync("config", "list", "--json")).Json();

        ShouldHave(json, "settings");

        var settings = json.GetProperty("settings").EnumerateArray().ToList();

        settings.Should().NotBeEmpty();

        foreach (var setting in settings)
        {
            ShouldHave(setting, "key", "description", "machineLocal");
        }
    }

    [BuiltCliFact]
    public async Task Config_get_explains_where_a_value_comes_from()
    {
        using var loadout = new LoadoutProcess();

        var json = (await loadout.RunAsync("config", "get", "default-agent", "--json")).Json();

        // Provenance is the point of this document: which file owns the value
        // and whether it travels between machines.
        ShouldHave(json, "key", "value", "description", "scope", "file");

        json.GetProperty("scope").GetString().Should().BeOneOf("machine", "shared");
    }

    [BuiltCliFact]
    public async Task Sessions_is_an_array_of_resumable_sessions()
    {
        using var loadout = new LoadoutProcess();

        var json = (await loadout.RunAsync("sessions", "--json")).Json();

        json.ValueKind.Should().Be(JsonValueKind.Array);

        foreach (var session in json.EnumerateArray())
        {
            ShouldHave(session, "agent", "id", "directory", "lastActive", "transcript");
        }
    }

    [BuiltCliFact]
    public async Task Drift_is_an_array_of_projects()
    {
        using var loadout = new LoadoutProcess();

        var json = (await loadout.RunAsync("drift", "--json")).Json();

        json.ValueKind.Should().Be(JsonValueKind.Array);

        foreach (var project in json.EnumerateArray())
        {
            ShouldHave(project, "project", "drifted", "overall", "findings");
        }
    }

    [BuiltCliFact]
    public async Task Workspace_status_reports_whether_it_is_configured_and_cloned()
    {
        using var loadout = new LoadoutProcess();

        ShouldHave(
            (await loadout.RunAsync("workspace", "status", "--json")).Json(),
            "configured",
            "cloned",
            "remote",
            "localPath",
            "projects");
    }

    [BuiltCliFact]
    public async Task Backup_list_wraps_its_sets()
    {
        using var loadout = new LoadoutProcess();

        var json = (await loadout.RunAsync("backup", "list", "--json")).Json();

        ShouldHave(json, "sets");
        json.GetProperty("sets").ValueKind.Should().Be(JsonValueKind.Array);
    }

    [BuiltCliFact]
    public async Task A_failure_reports_an_error_and_the_exit_code_in_the_document()
    {
        using var loadout = new LoadoutProcess();

        // No workspace is configured in a throwaway home, so this fails — which
        // is what is being checked. A script must be able to read the reason
        // from the document rather than parsing a human sentence off stderr.
        var run = await loadout.RunAsync("mcp", "list", "--json");

        run.ExitCode.Should().NotBe(0);

        var json = run.Json();

        ShouldHave(json, "error", "exitCode");

        json.GetProperty("exitCode").GetInt32().Should().Be(run.ExitCode,
            "the code in the document is the code the process returned");
    }

    [BuiltCliFact]
    public async Task A_failure_before_the_command_runs_is_still_a_document()
    {
        using var loadout = new LoadoutProcess();

        // 'profile list' requires a project. The argument binder rejects this
        // before any command exists, so it is answered by the parser's own
        // exception handler rather than by CommandOutput — a different path to
        // the same promise, and the one that used to write a sentence to stderr
        // and leave stdout empty. A script asking for JSON then had nothing at
        // all to read, and no way to tell that from a command that printed
        // nothing successfully.
        var run = await loadout.RunAsync("profile", "list", "--json");

        run.ExitCode.Should().NotBe(0);

        var json = run.Json();

        ShouldHave(json, "error", "exitCode");

        json.GetProperty("exitCode").GetInt32().Should().Be(run.ExitCode);

        // The reason, not just a shape: "it failed" in a well-formed document
        // is no more useful than the empty stdout it replaced.
        json.GetProperty("error").GetString().Should().Contain("project");
    }

    [BuiltCliFact]
    public async Task Version_is_reported_without_a_workspace()
    {
        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync("--version");

        run.ExitCode.Should().Be(0);
        run.StandardOutput.Trim().Should().MatchRegex(@"^\d+\.\d+\.\d+$");
    }

    [BuiltCliFact]
    public async Task Json_output_carries_no_escape_sequences()
    {
        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync("doctor", "--json");

        // Spectre markup or colour leaking into the document would break every
        // parser, and does not show up in a key-set assertion.
        run.StandardOutput.Should().NotContain("");
        run.StandardOutput.Should().NotContain("[green]");
    }
}
