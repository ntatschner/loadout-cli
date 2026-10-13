using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// What 'voice check' prints as JSON, which the docs audit and the drafting
/// prompts read, so its shape is a contract.
/// </summary>
[Collection(ContractCollection.Name)]
public sealed class VoiceContractTests
{
    [BuiltCliFact]
    public async Task A_check_reports_each_finding_with_its_rule_and_line_and_still_succeeds()
    {
        using var loadout = new LoadoutProcess();

        var shown = (await loadout.RunAsync("voice", "show", "--json")).Json();
        var personal = shown.GetProperty("layers").EnumerateArray()
            .First(l => l.GetProperty("layer").GetString() == "personal")
            .GetProperty("path").GetString()!;

        Directory.CreateDirectory(Path.GetDirectoryName(personal)!);
        await File.WriteAllTextAsync(personal, JsonSerializer.Serialize(new
        {
            guide = "prose/1",
            rules = new object[]
            {
                new
                {
                    id = "british",
                    statement = "Write British spellings.",
                    check = new { type = "prefer-spelling", spellings = new Dictionary<string, string> { ["color"] = "colour" } },
                },
                new { id = "warm", statement = "Sound warm." },
            },
        }));

        var page = Path.Combine(loadout.Home, "page.md");
        await File.WriteAllTextAsync(page, "# A page\n\nThe color is red.\n");

        var checkedRun = await loadout.RunAsync("voice", "check", page, "--json");

        checkedRun.ExitCode.Should().Be(0, "a finding reports and never fails the command");

        var json = checkedRun.Json();
        json.GetProperty("guide").GetString().Should().Be("prose/1");
        json.GetProperty("surface").GetString().Should().Be("doc");

        var check = json.GetProperty("checks").EnumerateArray().Single();
        var finding = check.GetProperty("findings").EnumerateArray().Single();
        finding.GetProperty("rule").GetString().Should().Be("british");
        finding.GetProperty("line").GetInt32().Should().Be(3);
        finding.GetProperty("gates").GetBoolean().Should().BeFalse();

        check.GetProperty("unchecked").EnumerateArray().Single()
            .GetProperty("rule").GetString().Should().Be("warm", "a judged rule is named, not passed quietly");
    }

    [BuiltCliFact]
    public async Task Learning_without_a_terminal_refuses_but_a_dry_run_shows_the_plan()
    {
        using var loadout = new LoadoutProcess();

        var repository = Path.Combine(loadout.Home, "notes");
        Directory.CreateDirectory(repository);
        await File.WriteAllTextAsync(Path.Combine(repository, "a.md"), "Written by hand.");
        (await loadout.RunAsync("voice", "mark", "a.md", "--repo", repository)).ExitCode.Should().Be(0);

        var refused = await loadout.RunAsync("voice", "learn", "--repo", repository);

        refused.ExitCode.Should().NotBe(0, "every rule is the person's decision, so it will not run unattended");
        (refused.StandardOutput + refused.StandardError).Should().Contain("--dry-run");

        var planned = (await loadout.RunAsync("voice", "learn", "--repo", repository, "--dry-run", "--json")).Json();

        planned.GetProperty("dryRun").GetBoolean().Should().BeTrue();
        planned.GetProperty("evidence").GetInt32().Should().Be(1, "the marked file, and no squash merges outside a repository");
        planned.GetProperty("estimate").GetProperty("calls").GetInt32().Should().Be(1);

        (await loadout.RunAsync("voice", "runs", "--json")).Json()
            .GetProperty("runs").GetArrayLength().Should().Be(0, "a dry run records nothing");
    }

    [BuiltCliFact]
    public async Task A_check_with_nothing_to_check_is_refused()
    {
        using var loadout = new LoadoutProcess();

        (await loadout.RunAsync("voice", "check")).ExitCode.Should().NotBe(0);
    }

    [BuiltCliFact]
    public async Task Marking_several_without_a_terminal_needs_yes_and_writes_nothing_until_given_it()
    {
        using var loadout = new LoadoutProcess();

        var repository = Path.Combine(loadout.Home, "notes");
        Directory.CreateDirectory(repository);
        await File.WriteAllTextAsync(Path.Combine(repository, "a.md"), "a");
        await File.WriteAllTextAsync(Path.Combine(repository, "b.md"), "b");

        var refused = await loadout.RunAsync("voice", "mark", "a.md", "b.md", "--repo", repository);

        refused.ExitCode.Should().NotBe(0);
        (refused.StandardOutput + refused.StandardError).Should().Contain("--yes");

        (await loadout.RunAsync("voice", "mark", "--list", "--repo", repository, "--json")).Json()
            .GetProperty("marks").GetArrayLength().Should().Be(0);

        (await loadout.RunAsync("voice", "mark", "a.md", "b.md", "--repo", repository, "--yes"))
            .ExitCode.Should().Be(0);

        (await loadout.RunAsync("voice", "mark", "--list", "--repo", repository, "--json")).Json()
            .GetProperty("marks").GetArrayLength().Should().Be(2);
    }

    [BuiltCliFact]
    public async Task The_docs_audit_reports_voice_warnings_apart_from_its_findings_and_still_succeeds()
    {
        using var loadout = new LoadoutProcess();

        await PersonalProfileAsync(loadout);

        var repository = Path.Combine(loadout.Home, "widget");
        Directory.CreateDirectory(repository);
        await File.WriteAllLinesAsync(Path.Combine(repository, "README.md"), ["# Widget", string.Empty, "The color is red."]);

        var audit = await loadout.RunAsync("docs", "audit", "--repo", repository, "--json");

        audit.ExitCode.Should().Be(0, "a learned rule warns and never fails the audit");

        var json = audit.Json();
        json.GetProperty("findings").EnumerateArray()
            .Should().NotContain(f => f.GetProperty("kind").GetString() == "voice",
                "anything counting stale references counts what it always counted");

        var voice = json.GetProperty("voice");
        voice.GetProperty("rules").GetInt32().Should().Be(2);

        var finding = voice.GetProperty("findings").EnumerateArray().Single();
        finding.GetProperty("path").GetString().Should().Be("README.md");
        finding.GetProperty("line").GetInt32().Should().Be(3);
        finding.GetProperty("detail").GetString().Should().StartWith("british: ");
    }

    [BuiltCliFact]
    public async Task Rendering_without_a_workspace_says_where_to_set_one_up()
    {
        using var loadout = new LoadoutProcess();

        await PersonalProfileAsync(loadout);

        var rendered = await loadout.RunAsync("voice", "render");

        rendered.ExitCode.Should().NotBe(0);
        (rendered.StandardOutput + rendered.StandardError).Should().Contain("loadout setup");
    }

    private static async Task PersonalProfileAsync(LoadoutProcess loadout)
    {
        var shown = (await loadout.RunAsync("voice", "show", "--json")).Json();
        var personal = shown.GetProperty("layers").EnumerateArray()
            .First(l => l.GetProperty("layer").GetString() == "personal")
            .GetProperty("path").GetString()!;

        Directory.CreateDirectory(Path.GetDirectoryName(personal)!);
        await File.WriteAllTextAsync(personal, JsonSerializer.Serialize(new
        {
            guide = "prose/1",
            rules = new object[]
            {
                new
                {
                    id = "british",
                    statement = "Write British spellings.",
                    check = new { type = "prefer-spelling", spellings = new Dictionary<string, string> { ["color"] = "colour" } },
                },
                new { id = "warm", statement = "Sound warm." },
            },
        }));
    }
}
