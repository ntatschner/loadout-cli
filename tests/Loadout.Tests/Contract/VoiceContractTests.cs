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
        json.GetProperty("guides").EnumerateArray().Select(g => g.GetString()).Should().Equal("prose/1");
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

    [BuiltCliFact]
    public async Task The_shipped_guide_is_listed_and_shown_and_drafting_asks_for_review_before_spending()
    {
        using var loadout = new LoadoutProcess();

        var listed = (await loadout.RunAsync("voice", "guide", "list", "--json")).Json();
        var prose = listed.GetProperty("guides").EnumerateArray().Single(g => g.GetProperty("id").GetString() == "prose");
        prose.GetProperty("reference").GetString().Should().Be("prose/1");
        prose.GetProperty("ships").GetBoolean().Should().BeTrue();

        var shown = (await loadout.RunAsync("voice", "guide", "show", "prose/1", "--json")).Json();
        shown.GetProperty("guide").GetProperty("evidence").GetProperty("citations").GetInt32().Should().Be(3);

        (await loadout.RunAsync("voice", "guide", "show", "nothing")).ExitCode.Should().NotBe(0);

        var unattended = await loadout.RunAsync("voice", "guide", "draft", "idiolect", "--about", "the phrases I reach for");

        unattended.ExitCode.Should().NotBe(0, "a draft is saved only once somebody has reviewed it");
        (unattended.StandardOutput + unattended.StandardError).Should().Contain("--dry-run");

        var json = await loadout.RunAsync("voice", "guide", "draft", "idiolect", "--about", "the phrases", "--json");

        json.ExitCode.Should().NotBe(0, "saving asks, so JSON output goes with a dry run");
        (json.StandardOutput + json.StandardError).Should().Contain("--json goes with --dry-run");
    }

    [BuiltCliFact]
    public async Task Transcripts_are_read_only_by_a_guide_that_lists_them_and_only_for_your_own_profile()
    {
        using var loadout = new LoadoutProcess();

        var prose = await loadout.RunAsync("voice", "learn", "--transcripts", "--dry-run");

        prose.ExitCode.Should().NotBe(0, "prose/1 learns sentence form, which prompts do not show");
        (prose.StandardOutput + prose.StandardError).Should().Contain("prose/1 does not read transcripts")
            .And.Contain("loadout voice guide draft", "the refusal says how to get a guide that does");

        var named = await loadout.RunAsync("voice", "learn", "--transcripts", "--layer", "named", "--style", "work", "--dry-run");

        named.ExitCode.Should().NotBe(0);
        (named.StandardOutput + named.StandardError).Should().Contain("personal profile");

        var unknown = await loadout.RunAsync("voice", "learn", "--guide", "nothing", "--dry-run");

        unknown.ExitCode.Should().NotBe(0);
        (unknown.StandardOutput + unknown.StandardError).Should().Contain("There is: prose");
    }

    [BuiltCliFact]
    public async Task Writing_is_checked_against_every_guide_unless_one_is_named()
    {
        using var loadout = new LoadoutProcess();

        var repository = Path.Combine(loadout.Home, "widget");
        Directory.CreateDirectory(repository);
        await File.WriteAllLinesAsync(Path.Combine(repository, "README.md"), ["# Widget", string.Empty, "The color is red."]);
        await Git(repository, "init");
        await Git(repository, "add", "-A");
        await Git(repository, "-c", "user.email=t@example.com", "-c", "user.name=T", "commit", "-m", "first");

        var added = await loadout.RunAsync("project", "add", repository, "--json");
        added.ExitCode.Should().Be(0, "the rest is about a workspace that exists: " + added.StandardError);

        // The workspace is two levels above the project's own directory in it.
        var slug = added.Json().GetProperty("id").GetString()!;
        var workspace = Path.GetDirectoryName(Path.GetDirectoryName(Directory
            .EnumerateDirectories(loadout.Home, slug, SearchOption.AllDirectories)
            .Single(path => Path.GetFileName(Path.GetDirectoryName(path)) == "projects")))!;

        var guides = Path.Combine(workspace, "voice-guides", "idiolect");
        Directory.CreateDirectory(guides);
        await File.WriteAllTextAsync(Path.Combine(guides, "1.json"), JsonSerializer.Serialize(new
        {
            id = "idiolect",
            version = 1,
            summary = "The words you reach for.",
            sources = new[] { "transcript" },
            surfaces = new[] { "doc", "commit", "pr" },
            checkTypes = new[] { "prefer-spelling" },
        }));

        var shown = (await loadout.RunAsync("voice", "show", "--guide", "idiolect", "--json")).Json();
        shown.GetProperty("guide").GetProperty("version").GetInt32().Should().Be(1);

        var personal = shown.GetProperty("layers").EnumerateArray()
            .First(l => l.GetProperty("layer").GetString() == "personal")
            .GetProperty("path").GetString()!;

        Path.GetFileName(personal).Should().Be("idiolect.json");
        Directory.CreateDirectory(Path.GetDirectoryName(personal)!);
        await File.WriteAllTextAsync(personal, JsonSerializer.Serialize(new
        {
            guide = "idiolect/1",
            rules = new object[]
            {
                new
                {
                    id = "british",
                    statement = "Write British spellings.",
                    check = new { type = "prefer-spelling", spellings = new Dictionary<string, string> { ["color"] = "colour" } },
                },
            },
        }));

        var readme = Path.Combine(repository, "README.md");

        var whole = (await loadout.RunAsync("voice", "check", readme, "--json")).Json();
        whole.GetProperty("guides").EnumerateArray().Select(g => g.GetString()).Should().Equal("prose/1", "idiolect/1");
        whole.GetProperty("checks").EnumerateArray().Single().GetProperty("findings").EnumerateArray()
            .Single().GetProperty("rule").GetString().Should().Be("british");

        var proseOnly = (await loadout.RunAsync("voice", "check", readme, "--guide", "prose", "--json")).Json();
        proseOnly.GetProperty("guides").EnumerateArray().Select(g => g.GetString()).Should().Equal("prose/1");
        proseOnly.GetProperty("checks").EnumerateArray().Single().GetProperty("findings").GetArrayLength().Should().Be(0);

        var audit = (await loadout.RunAsync("docs", "audit", "--repo", repository, "--json")).Json();
        audit.GetProperty("voice").GetProperty("findings").EnumerateArray()
            .Single().GetProperty("detail").GetString().Should().StartWith("british: ");
    }

    private static async Task<string> Git(string directory, params string[] arguments)
    {
        var start = new System.Diagnostics.ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = System.Diagnostics.Process.Start(start)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return output;
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
