using Loadout.Core.Backups;
using Loadout.Core.Configuration;
using Loadout.Core.Git;
using Loadout.Core.Instructions;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Models.Instructions;
using Loadout.Models.Platform;
using Loadout.Platform.Abstractions;
using Loadout.Platform.Common;
using Loadout.Platform.Linux;
using Loadout.Tests.Fakes;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// Covers the three services that decide what an agent is told and what it can
/// be trusted with: path-scoped rules, durable memory, and the backups that make
/// the mutating operations reversible.
/// <para>
/// All three read and write real files, so they are exercised against a real
/// temporary workspace rather than a mock filesystem: the failure modes that
/// matter here are file-shaped ones.
/// </para>
/// </summary>
public sealed class InstructionsTests : IAsyncLifetime
{
    private const string Slug = "starstats";

    private readonly string _root;
    private readonly ProcessLauncher _processes = new();

    private IWorkspaceManager _workspace = null!;
    private IRuleService _rules = null!;
    private IMemoryService _memory = null!;
    private IBackupService _backups = null!;
    private IPlatformPaths _paths = null!;

    public InstructionsTests() =>
        _root = Path.Combine(Path.GetTempPath(), "loadout-instr-" + Guid.NewGuid().ToString("N"));

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);

        var environment = new FakeEnvironmentProvider(
            Path.Combine(_root, "home"),
            new Dictionary<string, string>
            {
                ["XDG_CONFIG_HOME"] = Path.Combine(_root, "config"),
                ["XDG_DATA_HOME"] = Path.Combine(_root, "data"),
                ["XDG_STATE_HOME"] = Path.Combine(_root, "state"),
                ["XDG_CACHE_HOME"] = Path.Combine(_root, "cache"),
            });

        var permissions = new NoOpFilePermissions();

        _paths = new LinuxPaths(
            environment,
            permissions,
            new HostPlatform(
                HostOperatingSystem.Linux,
                System.Runtime.InteropServices.Architecture.X64,
                "test",
                "TEST-MACHINE"));

        _paths.EnsureDirectoriesExist();

        var git = new GitManager(_processes, new ExecutableResolver(environment, []));
        var yaml = new YamlStore(permissions);

        _workspace = new WorkspaceManager(_paths, git, yaml, TimeProvider.System);
        _rules = new RuleService();
        _memory = new MemoryService(TimeProvider.System);
        _backups = new BackupService(_paths, permissions, yaml, TimeProvider.System);

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp tree is not worth failing the run over.
        }

        return Task.CompletedTask;
    }

    private void WriteRule(string scope, string name, string contents)
    {
        var directory = scope == "global"
            ? Path.Combine(_workspace.LocalPath, "global", "rules")
            : Path.Combine(_workspace.LocalPath, "projects", Slug, "rules");

        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name + ".md"), contents);
    }

    private string WriteTopic(string name, string contents)
    {
        var directory = Path.Combine(_workspace.LocalPath, "projects", Slug, "memory");
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, name + ".md");
        File.WriteAllText(path, contents);

        return path;
    }

    [Fact]
    public async Task A_project_rule_overrides_a_workspace_rule_of_the_same_name()
    {
        WriteRule("global", "style", "---\ndescription: house style\n---\nUse tabs.");
        WriteRule("project", "style", "---\ndescription: project style\n---\nUse spaces.");

        var rules = await _rules.LoadAsync(_workspace.LocalPath, Slug);

        rules.Succeeded.Should().BeTrue();
        rules.Value!.Should().ContainSingle(r => r.Name == "style");
        rules.Value!.Single(r => r.Name == "style").Description.Should().Be("project style");
    }

    [Fact]
    public async Task Frontmatter_decides_when_a_rule_loads()
    {
        WriteRule("project", "sql", "---\ndescription: db\nglobs: src/Data/**/*.cs\n---\nBody.");
        WriteRule("project", "always", "---\ndescription: core\nalwaysApply: true\n---\nBody.");

        var loaded = await _rules.LoadAsync(_workspace.LocalPath, Slug);

        var forFrontend = _rules.Select(loaded.Value!, ["src/Web/Home.razor"]);
        var forData = _rules.Select(loaded.Value!, ["src/Data/Migrations/Add.cs"]);

        // The scoped rule costs nothing while nobody is touching the database,
        // which is the entire point of scoping it.
        forFrontend.Select(r => r.Name).Should().BeEquivalentTo("always");
        forData.Select(r => r.Name).Should().BeEquivalentTo("always", "sql");
    }

    [Theory]
    [InlineData("**/*.cs", "src/Core/Deep/File.cs", true)]
    [InlineData("*.cs", "src/Core/File.cs", false)]
    [InlineData("src/*/*.cs", "src/Core/File.cs", true)]
    [InlineData("src/**", "src/a/b/c.txt", true)]
    [InlineData("docs/**", "src/a.txt", false)]
    public void A_doubled_star_crosses_directories_and_a_single_one_does_not(
        string glob,
        string path,
        bool expected) =>
        RuleService.Matches(glob, path).Should().Be(expected);

    [Fact]
    public void A_rule_written_with_forward_slashes_matches_a_windows_path() =>
        // Rules are committed to a shared workspace and read on all three
        // platforms, so the separator a colleague happened to type must not
        // decide whether the rule applies.
        RuleService.Matches("src/**/*.cs", @"src\Core\File.cs").Should().BeTrue();

    [Fact]
    public async Task An_unscoped_rule_is_reported_rather_than_guessed_at()
    {
        WriteRule("project", "notes", "Just some prose with no frontmatter at all.");

        var loaded = await _rules.LoadAsync(_workspace.LocalPath, Slug);
        var budget = _rules.Budget(loaded.Value!, coreBytes: 0);

        budget.UnscopedRules.Should().ContainSingle(r => r.Name == "notes");
        budget.AlwaysApplyRules.Should().BeEmpty();
        budget.ScopedRules.Should().BeEmpty();
    }

    [Fact]
    public async Task The_always_loaded_budget_counts_core_instructions_and_always_apply_rules()
    {
        WriteRule("project", "always", "---\nalwaysApply: true\n---\n" + new string('x', 1000));
        WriteRule("project", "scoped", "---\nglobs: '*.md'\n---\n" + new string('y', 5000));

        var loaded = await _rules.LoadAsync(_workspace.LocalPath, Slug);
        var budget = _rules.Budget(loaded.Value!, coreBytes: 2000);

        budget.AlwaysLoadedBytes.Should().BeGreaterThan(3000).And.BeLessThan(3200);
        budget.ScopedBytes.Should().BeGreaterThan(5000);
    }

    [Fact]
    public async Task Writing_memory_creates_a_topic_and_an_index()
    {
        var written = await _memory.WriteAsync(
            _workspace.LocalPath,
            Slug,
            "Build Quirks",
            "things that surprise people about the build",
            MemoryKind.Lesson,
            ["The first build after a clean takes four minutes because the analyzers warm up."]);

        written.Succeeded.Should().BeTrue();
        written.Value!.Name.Should().Be("build-quirks");
        written.Value.Kind.Should().Be(MemoryKind.Lesson);

        var index = Path.Combine(_workspace.LocalPath, "projects", Slug, "memory", "MEMORY.md");
        File.Exists(index).Should().BeTrue();
        File.ReadAllText(index).Should().Contain("build-quirks.md");
    }

    [Fact]
    public async Task Memory_refuses_a_credential_rather_than_recording_and_flagging_it()
    {
        var written = await _memory.WriteAsync(
            _workspace.LocalPath,
            Slug,
            "deploy",
            "how to deploy",
            MemoryKind.Reference,
            ["Use the token ghp_abcdefghijklmnopqrstuvwxyz0123 for the release workflow."]);

        // Memory is committed to a shared repository. Writing it and reporting
        // it afterwards would mean the disclosure had already happened.
        written.Failed.Should().BeTrue();
        written.ExitCode.Should().Be(ExitCode.PolicyViolation);
        written.Error.Should().Contain("GitHub token");
        written.Error.Should().NotContain("ghp_abcdefghijklmnopqrstuvwxyz0123");

        Directory.Exists(Path.Combine(_workspace.LocalPath, "projects", Slug, "memory"))
            .Should().BeFalse();
    }

    [Fact]
    public async Task The_index_is_not_treated_as_a_topic()
    {
        await _memory.WriteAsync(_workspace.LocalPath, Slug, "one", "first", MemoryKind.Project, ["A fact about the build."]);

        var topics = await _memory.ListAsync(_workspace.LocalPath, Slug);

        topics.Value!.Should().ContainSingle().Which.Name.Should().Be("one");
    }

    [Fact]
    public async Task The_audit_reports_a_credential_that_reached_memory_by_another_route()
    {
        // A file written by hand, or by an older version, bypasses the check on
        // the write path. The audit is the backstop.
        WriteTopic("leaked", "---\ndescription: notes\n---\n- key sk-ant-api03-abcdefghijklmnopqrstuvwxyz012345 works\n");

        var audit = await _memory.AuditAsync(_workspace.LocalPath, Slug);

        audit.Value!.Verdict.Should().Be("ACTION REQUIRED");
        audit.Value.Errors.Should().ContainSingle(f => f.Kind == "credential");
        audit.Value.Errors.Single().Detail.Should().NotContain("sk-ant-api03");
    }

    [Fact]
    public async Task The_audit_finds_a_fact_repeated_in_two_topics()
    {
        const string fact = "- The workspace repository always wins when it disagrees with the code.";

        WriteTopic("a", "---\ndescription: a\n---\n" + fact);
        WriteTopic("b", "---\ndescription: b\n---\n" + fact);

        var audit = await _memory.AuditAsync(_workspace.LocalPath, Slug);

        audit.Value!.Findings.Should().Contain(f => f.Kind == "duplicate");
    }

    [Fact]
    public async Task Short_bullets_are_not_compared_for_duplication()
    {
        // "Use British spelling" repeating in two topics is not a defect, and
        // reporting it would train people to ignore the audit.
        WriteTopic("a", "---\ndescription: a\n---\n- Use spaces.");
        WriteTopic("b", "---\ndescription: b\n---\n- Use spaces.");

        var audit = await _memory.AuditAsync(_workspace.LocalPath, Slug);

        audit.Value!.Findings.Should().NotContain(f => f.Kind == "duplicate");
    }

    [Fact]
    public async Task The_audit_reports_an_index_pointing_at_a_deleted_topic()
    {
        WriteTopic("kept", "---\ndescription: kept\n---\n- A fact worth keeping around here.");
        WriteTopic("MEMORY", "# Index\n\n- [gone](gone.md) - a topic that no longer exists\n");

        var audit = await _memory.AuditAsync(_workspace.LocalPath, Slug);

        audit.Value!.Findings.Should().Contain(f => f.Kind == "index-dead-link");
    }

    [Fact]
    public async Task Reindexing_repairs_an_index_that_has_drifted()
    {
        WriteTopic("alpha", "---\ndescription: the alpha topic\n---\n- A fact.");
        WriteTopic("MEMORY", "# Index\n\n- [gone](gone.md) - nothing\n");

        await _memory.RebuildIndexAsync(_workspace.LocalPath, Slug);

        var index = File.ReadAllText(
            Path.Combine(_workspace.LocalPath, "projects", Slug, "memory", "MEMORY.md"));

        index.Should().Contain("alpha.md").And.NotContain("gone.md");
    }

    [Fact]
    public async Task A_topic_written_as_prose_is_not_treated_as_empty()
    {
        // The shape memory actually arrives in. Only counting bullets reported
        // a page of carefully written reasoning as holding nothing, which then
        // had the audit flag it and an import refuse to bring it across.
        WriteTopic("prose", """
---
description: no marketing in the repository
---

Do not commit marketing material to this repository: launch copy, campaign
checklists or any sales-facing writing.

**Why:** the repository is for what the product ships or builds from.

**How to apply:** deliver launch copy in the conversation and write files
outside the repository.
""");

        var topics = await _memory.ListAsync(_workspace.LocalPath, Slug);

        topics.Value!.Single().Facts.Should().HaveCount(3);
    }

    [Fact]
    public async Task A_wrapped_paragraph_is_one_fact_rather_than_four()
    {
        WriteTopic("wrapped", """
---
description: wrapped
---

The migration runner refuses to reorder a step once it has run, because the
checksum it recorded no longer matches and it cannot tell an edit from a
corruption.
""");

        var topics = await _memory.ListAsync(_workspace.LocalPath, Slug);

        topics.Value!.Single().Facts.Should().ContainSingle()
            .Which.Should().Contain("refuses to reorder a step once it has run");
    }

    [Fact]
    public async Task A_topic_with_only_frontmatter_still_holds_nothing()
    {
        WriteTopic("bare", "---\ndescription: nothing\n---\n");

        var topics = await _memory.ListAsync(_workspace.LocalPath, Slug);

        topics.Value!.Single().Facts.Should().BeEmpty();
    }

    [Theory]
    [InlineData(@"D:\git\RSIStarCitizenTools\StarStats", "D--git-RSIStarCitizenTools-StarStats")]
    [InlineData(@"D:\git\home-servers-build", "D--git-home-servers-build")]
    [InlineData("/home/me/work/thing", "-home-me-work-thing")]
    [InlineData(@"D:\git\dotted.name", "D--git-dotted-name")]
    public void The_agents_own_directory_name_is_reproduced_exactly(string path, string expected) =>
        // It has to match what the other tool already wrote, byte for byte, or
        // the memory it recorded is simply never found.
        MemoryImporter.DerivedSlug(path).Should().Be(expected);

    [Fact]
    public async Task Importing_brings_topics_in_and_rebuilds_the_index()
    {
        var source = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(source);

        await File.WriteAllTextAsync(
            Path.Combine(source, "build-quirks.md"),
            "---\ndescription: the build\n---\n\n- The first build takes four minutes.");

        await File.WriteAllTextAsync(Path.Combine(source, "MEMORY.md"), "- [gone](gone.md) - stale");

        var importer = new MemoryImporter(
            new FakeEnvironmentProvider(_root, new Dictionary<string, string>()), _memory);

        var imported = await importer.ImportAsync(_workspace.LocalPath, Slug, source, apply: true);

        imported.Value!.Imported.Should().ContainSingle(t => t.Name == "build-quirks");

        var index = await File.ReadAllTextAsync(
            Path.Combine(_workspace.LocalPath, "projects", Slug, "memory", "MEMORY.md"));

        // The old index is not copied: it lists files that may not all have
        // come across, so it is rebuilt from what actually arrived.
        index.Should().Contain("build-quirks").And.NotContain("gone.md");
    }

    [Fact]
    public async Task An_import_never_carries_a_credential_into_the_repository()
    {
        var source = Path.Combine(_root, "leaky");
        Directory.CreateDirectory(source);

        await File.WriteAllTextAsync(
            Path.Combine(source, "deploy.md"),
            "---\ndescription: deploy\n---\n\n- Use ghp_abcdefghijklmnopqrstuvwxyz0123 to release.");

        var importer = new MemoryImporter(
            new FakeEnvironmentProvider(_root, new Dictionary<string, string>()), _memory);

        var imported = await importer.ImportAsync(_workspace.LocalPath, Slug, source, apply: true);

        // The workspace is a Git repository. Importing this would commit the
        // credential and publish it on the next push, which no later audit can
        // undo.
        imported.Value!.Imported.Should().BeEmpty();
        imported.Value.Skipped.Should().ContainKey("deploy");
        imported.Value.Skipped["deploy"].Should().Contain("GitHub token")
            .And.NotContain("ghp_abcdefghijklmnopqrstuvwxyz0123");

        File.Exists(Path.Combine(_workspace.LocalPath, "projects", Slug, "memory", "deploy.md"))
            .Should().BeFalse();
    }

    [Fact]
    public async Task An_import_never_overwrites_a_topic_already_in_the_workspace()
    {
        WriteTopic("build-quirks", "---\ndescription: mine\n---\n\n- The workspace copy.");

        var source = Path.Combine(_root, "second");
        Directory.CreateDirectory(source);

        await File.WriteAllTextAsync(
            Path.Combine(source, "build-quirks.md"),
            "---\ndescription: theirs\n---\n\n- The imported copy.");

        var importer = new MemoryImporter(
            new FakeEnvironmentProvider(_root, new Dictionary<string, string>()), _memory);

        var imported = await importer.ImportAsync(_workspace.LocalPath, Slug, source, apply: true);

        imported.Value!.Skipped.Should().ContainKey("build-quirks");

        var kept = await File.ReadAllTextAsync(
            Path.Combine(_workspace.LocalPath, "projects", Slug, "memory", "build-quirks.md"));

        kept.Should().Contain("The workspace copy.");
    }

    [Fact]
    public async Task An_import_preview_writes_nothing()
    {
        var source = Path.Combine(_root, "preview");
        Directory.CreateDirectory(source);

        await File.WriteAllTextAsync(
            Path.Combine(source, "topic.md"),
            "---\ndescription: d\n---\n\n- A fact worth importing here.");

        var importer = new MemoryImporter(
            new FakeEnvironmentProvider(_root, new Dictionary<string, string>()), _memory);

        var preview = await importer.ImportAsync(_workspace.LocalPath, Slug, source, apply: false);

        preview.Value!.Imported.Should().ContainSingle();

        File.Exists(Path.Combine(_workspace.LocalPath, "projects", Slug, "memory", "topic.md"))
            .Should().BeFalse();
    }

    [Fact]
    public async Task A_backup_restores_a_file_the_operation_overwrote()
    {
        var file = Path.Combine(_root, "notes.md");
        File.WriteAllText(file, "original");

        var captured = await _backups.CaptureAsync("test", "overwrite", [file]);
        captured.Succeeded.Should().BeTrue();

        File.WriteAllText(file, "clobbered");

        var restored = await _backups.RestoreAsync(captured.Value!.Id, apply: true);

        restored.Succeeded.Should().BeTrue(restored.Error ?? string.Empty);
        File.ReadAllText(file).Should().Be("original");
    }

    [Fact]
    public async Task A_restore_removes_a_file_the_operation_created()
    {
        var created = Path.Combine(_root, "created.md");

        // Captured while absent. This is the case the toolkit this is modelled
        // on gets wrong: rolling back left every created file behind as debris.
        var captured = await _backups.CaptureAsync("test", "creates", [created]);
        File.WriteAllText(created, "made by the operation");

        var restored = await _backups.RestoreAsync(captured.Value!.Id, apply: true);

        restored.Value!.Removed.Should().Contain(created);
        File.Exists(created).Should().BeFalse();
    }

    [Fact]
    public async Task A_dry_run_changes_nothing()
    {
        var file = Path.Combine(_root, "dry.md");
        File.WriteAllText(file, "original");

        var captured = await _backups.CaptureAsync("test", "dry", [file]);
        File.WriteAllText(file, "changed");

        var report = await _backups.RestoreAsync(captured.Value!.Id, apply: false);

        report.Value!.Applied.Should().BeFalse();
        report.Value.Restored.Should().Contain(file);
        File.ReadAllText(file).Should().Be("changed");
    }

    [Fact]
    public async Task A_corrupted_payload_fails_before_anything_is_written()
    {
        var first = Path.Combine(_root, "first.md");
        var second = Path.Combine(_root, "second.md");
        File.WriteAllText(first, "first original");
        File.WriteAllText(second, "second original");

        var captured = await _backups.CaptureAsync("test", "corrupt", [first, second]);

        var payloads = Directory
            .EnumerateFiles(_paths.Paths.State, "*", SearchOption.AllDirectories)
            .Where(p => p.Contains(captured.Value!.Id, StringComparison.Ordinal)
                && !p.EndsWith("manifest.yaml", StringComparison.Ordinal))
            .ToList();

        payloads.Should().NotBeEmpty();
        File.WriteAllText(payloads[0], "tampered");

        File.WriteAllText(first, "changed");
        File.WriteAllText(second, "changed");

        var restored = await _backups.RestoreAsync(captured.Value!.Id, apply: true);

        // Half a restore is worse than none: the tree would be left in a state
        // that never existed.
        restored.Failed.Should().BeTrue();
        File.ReadAllText(first).Should().Be("changed");
        File.ReadAllText(second).Should().Be("changed");
    }

    [Fact]
    public async Task A_set_can_be_addressed_by_an_unambiguous_prefix()
    {
        var file = Path.Combine(_root, "prefix.md");
        File.WriteAllText(file, "content");

        var captured = await _backups.CaptureAsync("test", "prefix", [file]);
        var prefix = captured.Value!.Id[..8];

        var found = await _backups.GetAsync(prefix);

        found.Succeeded.Should().BeTrue();
        found.Value!.Id.Should().Be(captured.Value.Id);
    }

    [Fact]
    public async Task A_restore_names_the_settings_it_would_take_away()
    {
        var settings = Path.Combine(_root, "settings.json");
        File.WriteAllText(settings, """{"model":"opus","permissions":{"allow":["Bash"]}}""");

        var captured = await _backups.CaptureAsync("test", "settings", [settings]);

        // A key added after the snapshot. A whole-file restore drops it, every
        // digest still matches, and nothing else in the report would say so.
        File.WriteAllText(
            settings,
            """{"model":"opus","toolSearch":true,"permissions":{"allow":["Bash"]}}""");

        var report = await _backups.RestoreAsync(captured.Value!.Id, apply: false);

        report.Value!.Dropped.Select(d => d.KeyPath).Should().Contain("toolSearch");
    }

    [Fact]
    public async Task Drift_reports_key_paths_and_never_values()
    {
        var settings = Path.Combine(_root, "secrets.json");
        File.WriteAllText(settings, """{"kept":1}""");

        var captured = await _backups.CaptureAsync("test", "secrets", [settings]);
        File.WriteAllText(settings, """{"kept":1,"apiKeyHelper":"echo sk-ant-not-a-real-key-12345"}""");

        var report = await _backups.RestoreAsync(captured.Value!.Id, apply: false);

        var drift = report.Value!.Drift;

        drift.Should().Contain(d => d.KeyPath == "apiKeyHelper");
        drift.Should().OnlyContain(d => !d.KeyPath.Contains("sk-ant", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Removing_an_object_reports_the_object_and_not_every_field_in_it()
    {
        var settings = Path.Combine(_root, "nested.json");
        File.WriteAllText(settings, """{"a":1}""");

        var captured = await _backups.CaptureAsync("test", "nested", [settings]);
        File.WriteAllText(settings, """{"a":1,"hooks":{"one":"x","two":"y","three":"z"}}""");

        var report = await _backups.RestoreAsync(captured.Value!.Id, apply: false);

        // Four lines saying the same thing would bury the one that matters.
        report.Value!.Dropped.Select(d => d.KeyPath).Should().BeEquivalentTo("hooks");
    }

    [Fact]
    public async Task An_unparseable_file_is_not_reported_as_having_lost_every_key()
    {
        var settings = Path.Combine(_root, "broken.json");
        File.WriteAllText(settings, """{"a":1}""");

        var captured = await _backups.CaptureAsync("test", "broken", [settings]);
        File.WriteAllText(settings, "{ this is not json at all");

        var report = await _backups.RestoreAsync(captured.Value!.Id, apply: false);

        report.Value!.Drift.Should().BeEmpty();
    }

    [Fact]
    public async Task A_plain_text_file_produces_no_drift()
    {
        var notes = Path.Combine(_root, "notes.txt");
        File.WriteAllText(notes, "original");

        var captured = await _backups.CaptureAsync("test", "text", [notes]);
        File.WriteAllText(notes, "changed");

        var report = await _backups.RestoreAsync(captured.Value!.Id, apply: false);

        report.Value!.Drift.Should().BeEmpty();
    }

    [Fact]
    public async Task Cleanup_removes_a_topic_that_holds_no_facts()
    {
        // Frontmatter and nothing else. Prose is not emptiness: a topic that
        // makes one point at length is stating a fact just as much as a list
        // is, and deleting it as empty would lose exactly the notes people
        // write when the reasoning is the valuable part.
        WriteTopic("empty", "---\ndescription: nothing here\n---\n");
        WriteTopic("kept", "---\ndescription: kept\n---\n- A fact worth keeping around here.");

        var cleaned = await _memory.CleanAsync(_workspace.LocalPath, Slug, apply: true);

        cleaned.Value!.RemovedTopics.Should().BeEquivalentTo("empty");
        (await _memory.ListAsync(_workspace.LocalPath, Slug)).Value!
            .Select(t => t.Name).Should().BeEquivalentTo("kept");
    }

    [Fact]
    public async Task Cleanup_removes_a_fact_repeated_word_for_word()
    {
        const string fact = "- The workspace repository wins when it disagrees with the code.";

        WriteTopic("a", $"---\ndescription: a\n---\n{fact}\n{fact}\n");

        var cleaned = await _memory.CleanAsync(_workspace.LocalPath, Slug, apply: true);

        cleaned.Value!.RemovedBullets.Should().ContainSingle();
        (await _memory.ListAsync(_workspace.LocalPath, Slug)).Value!
            .Single().Facts.Should().ContainSingle();
    }

    [Fact]
    public async Task Cleanup_leaves_two_facts_that_merely_say_similar_things()
    {
        WriteTopic("a", """
---
description: a
---
- The build fails when the schema drifts from the migrations.
- The build will fail if the schema and the migrations disagree.
""");

        var cleaned = await _memory.CleanAsync(_workspace.LocalPath, Slug, apply: true);

        // Deciding which wording is the right one is a judgement, and getting
        // it wrong loses the better of the two permanently.
        cleaned.Value!.RemovedBullets.Should().BeEmpty();
    }

    [Fact]
    public async Task Cleanup_prunes_an_index_line_pointing_at_nothing()
    {
        WriteTopic("kept", "---\ndescription: kept\n---\n- A fact worth keeping around here.");
        WriteTopic("MEMORY", "# Index\n\n- [kept](kept.md) - kept\n- [gone](gone.md) - gone\n");

        var cleaned = await _memory.CleanAsync(_workspace.LocalPath, Slug, apply: true);

        cleaned.Value!.RemovedIndexLines.Should().ContainSingle(l => l.Contains("gone"));

        var index = await File.ReadAllTextAsync(
            Path.Combine(_workspace.LocalPath, "projects", Slug, "memory", "MEMORY.md"));

        index.Should().Contain("kept.md").And.NotContain("gone.md");
    }

    [Fact]
    public async Task A_cleanup_preview_changes_nothing()
    {
        WriteTopic("empty", "---\ndescription: nothing\n---\n");

        var preview = await _memory.CleanAsync(_workspace.LocalPath, Slug, apply: false);

        preview.Value!.RemovedTopics.Should().BeEquivalentTo("empty");
        preview.Value.Applied.Should().BeFalse();

        File.Exists(Path.Combine(_workspace.LocalPath, "projects", Slug, "memory", "empty.md"))
            .Should().BeTrue();
    }
}
