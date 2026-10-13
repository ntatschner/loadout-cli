using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Core.Configuration;
using Loadout.Core.Instructions;
using Loadout.Core.Voice;
using Loadout.Models.Configuration;
using Loadout.Models.Instructions;
using Loadout.Models.Projects;
using Loadout.Models.Results;
using Loadout.Models.Voice;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Applying a learned voice: rendered into a style specialist, loaded where its
/// style is in force, guarded against hand edits, and checked by the docs audit.
/// </summary>
/// <remarks>
/// Rendered into a real workspace on disk and loaded by the real library, so
/// what is tested is what a launch would read.
/// </remarks>
public sealed class VoiceRenderingTests : IDisposable
{
    private const string Slug = "demo";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "loadout-voice-render-" + Guid.NewGuid().ToString("N"));
    private readonly VoiceStore _store;
    private readonly VoiceGuide _guide = VoiceGuides.Prose();

    public VoiceRenderingTests()
    {
        _store = new VoiceStore(Path.Combine(_root, "config"), Path.Combine(_root, "state"), Workspace);
    }

    private string Workspace => Path.Combine(_root, "workspace");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static VoiceProfile Profile(params VoiceRule[] rules) => new() { Guide = "prose/1", Rules = [.. rules] };

    private static VoiceRule Rule(string id, string statement, params string[] surfaces) => new()
    {
        Id = id,
        Statement = statement,
        Surfaces = [.. surfaces],
        Citations = [new VoiceCitation { Source = "mark", Reference = "docs/private-notes.md", Excerpt = "an excerpt of my own writing" }],
    };

    private async Task SaveAsync(StyleLayer layer, string name, VoiceProfile profile) =>
        (await VoiceStore.WriteProfileAsync(_store.PathOf(layer, name, _guide.Id, Slug)!, profile)).Succeeded.Should().BeTrue();

    private async Task<VoiceRendered> RenderAsync(
        StyleLayer layer = StyleLayer.Personal, string name = CodingStyles.Personal, bool force = false, bool dryRun = false)
    {
        var rendered = await _store.RenderAsync(layer, name, _guide, Slug, force, dryRun);

        rendered.Failed.Should().BeFalse(rendered.Error);

        return rendered.Value!;
    }

    private async Task<EffectiveInstructions> ResolveAsync(string? style) =>
        new SpecialistResolver().Resolve(new SpecialistRequest(
            await new SpecialistLibrary().LoadAsync(Workspace, Slug),
            Task: "Fix the null reference.",
            Evidence: RepositoryEvidence.None,
            Style: style));

    // ------------------------------------------------------------- loading

    [Fact]
    public async Task A_rendered_voice_loads_wherever_its_style_is_in_force()
    {
        await SaveAsync(StyleLayer.Personal, CodingStyles.Personal, Profile(Rule("british", "Write British spellings.")));
        await SaveAsync(StyleLayer.Named, "work", Profile(Rule("plain", "Say it plainly.")));
        await SaveAsync(StyleLayer.Codebase, CodingStyles.Codebase, Profile(Rule("short", "Keep commit subjects short.")));

        (await RenderAsync()).State.Should().Be(VoiceRenderState.Written);
        (await RenderAsync(StyleLayer.Named, "work")).State.Should().Be(VoiceRenderState.Written);
        (await RenderAsync(StyleLayer.Codebase, CodingStyles.Codebase)).State.Should().Be(VoiceRenderState.Written);

        var unchosen = (await ResolveAsync(style: null)).Selected.Select(s => s.Specialist.Id).ToList();

        unchosen.Should().Contain(["style.personal.voice", "style.codebase.voice"]);
        unchosen.Should().NotContain("style.work.voice", "a named style's voice is in force only where the style is");

        var chosen = await ResolveAsync(style: "work");

        chosen.Selected.Select(s => s.Specialist.Id)
            .Should().ContainInOrder("style.personal.voice", "style.work.voice", "style.codebase.voice");
        chosen.Selected.Single(s => s.Specialist.Id == "style.work.voice").Reason
            .Should().Be("the 'work' style chosen for this work");

        var catalogue = await new SpecialistLibrary().LoadAsync(Workspace, Slug);

        catalogue.Findings.Where(f => f.Rule is { } id && id.EndsWith(".voice", StringComparison.Ordinal))
            .Should().BeEmpty("a rendered voice is a well-formed style part that needs no evidence to load");
    }

    [Fact]
    public async Task Only_statements_are_rendered_never_the_writing_they_were_learned_from()
    {
        await SaveAsync(StyleLayer.Personal, CodingStyles.Personal, Profile(
            Rule("british", "Write British spellings."),
            Rule("subject", "Start a commit subject with a verb.", VoiceGuides.Commit)));

        var rendered = await RenderAsync();
        var text = await File.ReadAllTextAsync(rendered.Path);

        text.Should().Contain("- Write British spellings.\n");
        text.Should().Contain("- Start a commit subject with a verb. *(commit messages only)*");
        text.Should().NotContain("an excerpt of my own writing").And.NotContain("private-notes",
            "citations are provenance, and the rendered file travels with the workspace");

        text.Should().Contain("loadout voice check --message");
        text.Should().Contain("fix what is found once");
        text.Should().Contain("never in the commit message or the pull request");

        rendered.EstimatedTokens.Should().BeGreaterThan(0);
        rendered.Path.Should().Be(Path.Combine(Workspace, "global", "specialists", "style.personal.voice.md"));
    }

    // --------------------------------------------------------------- guard

    [Fact]
    public async Task A_rendered_file_changed_by_hand_is_left_alone_unless_forced()
    {
        await SaveAsync(StyleLayer.Personal, CodingStyles.Personal, Profile(Rule("british", "Write British spellings.")));
        var path = (await RenderAsync()).Path;

        await File.AppendAllTextAsync(path, "- A rule I added by hand.\n");
        await SaveAsync(StyleLayer.Personal, CodingStyles.Personal, Profile(Rule("plain", "Say it plainly.")));

        (await RenderAsync()).State.Should().Be(VoiceRenderState.HandEdited);
        (await File.ReadAllTextAsync(path)).Should().Contain("A rule I added by hand.");

        (await RenderAsync(force: true)).State.Should().Be(VoiceRenderState.Written);
        (await File.ReadAllTextAsync(path)).Should().NotContain("by hand").And.Contain("Say it plainly.");
    }

    [Fact]
    public async Task A_file_that_was_never_rendered_is_not_overwritten()
    {
        var path = _store.SpecialistPath(StyleLayer.Personal, CodingStyles.Personal, Slug)!;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "---\nid: style.personal.voice\nkind: style\n---\n\n- Mine.\n");

        await SaveAsync(StyleLayer.Personal, CodingStyles.Personal, Profile(Rule("british", "Write British spellings.")));

        (await RenderAsync()).State.Should().Be(VoiceRenderState.HandEdited);
        (await File.ReadAllTextAsync(path)).Should().Contain("- Mine.");
    }

    [Fact]
    public async Task Rendering_the_same_profile_again_changes_nothing_even_after_a_checkout_changed_its_line_endings()
    {
        await SaveAsync(StyleLayer.Personal, CodingStyles.Personal, Profile(Rule("british", "Write British spellings.")));
        var path = (await RenderAsync()).Path;

        await File.WriteAllTextAsync(path, (await File.ReadAllTextAsync(path)).Replace("\n", "\r\n", StringComparison.Ordinal));

        VoiceRendering.IsHandEdited(await File.ReadAllTextAsync(path)).Should().BeFalse();
        (await RenderAsync()).State.Should().Be(VoiceRenderState.Unchanged);
    }

    [Fact]
    public async Task A_profile_with_no_rules_left_takes_its_rendered_file_away()
    {
        await SaveAsync(StyleLayer.Personal, CodingStyles.Personal, Profile(Rule("british", "Write British spellings.")));
        var path = (await RenderAsync()).Path;

        await SaveAsync(StyleLayer.Personal, CodingStyles.Personal, Profile());

        (await RenderAsync(dryRun: true)).State.Should().Be(VoiceRenderState.Removed);
        File.Exists(path).Should().BeTrue("a dry run changes nothing");

        (await RenderAsync()).State.Should().Be(VoiceRenderState.Removed);
        File.Exists(path).Should().BeFalse();

        (await RenderAsync()).State.Should().Be(VoiceRenderState.Nothing);
    }

    [Fact]
    public async Task A_dry_run_writes_nothing()
    {
        await SaveAsync(StyleLayer.Personal, CodingStyles.Personal, Profile(Rule("british", "Write British spellings.")));

        var rendered = await RenderAsync(dryRun: true);

        rendered.State.Should().Be(VoiceRenderState.Written);
        File.Exists(rendered.Path).Should().BeFalse();
    }

    [Fact]
    public async Task Without_a_workspace_there_is_nowhere_to_render_into()
    {
        var store = new VoiceStore(Path.Combine(_root, "config"), Path.Combine(_root, "state"), workspaceRoot: null);

        var rendered = await store.RenderAsync(StyleLayer.Personal, CodingStyles.Personal, _guide, Slug);

        rendered.Failed.Should().BeTrue();
        rendered.Error.Should().Contain("loadout setup");
    }

    [Fact]
    public async Task A_voice_over_the_core_ceiling_is_reported()
    {
        var rules = Enumerable.Range(0, 120)
            .Select(i => Rule($"r{i}", $"Rule number {i}, which goes on for a while so that it costs something."))
            .ToArray();

        await SaveAsync(StyleLayer.Personal, CodingStyles.Personal, Profile(rules));
        await RenderAsync();

        var catalogue = await new SpecialistLibrary().LoadAsync(Workspace, Slug);

        catalogue.Findings.Should().Contain(f => f.Rule == "style.personal.voice" && f.Kind == "style-core-too-large",
            "a voice loads with its core on every launch");
    }

    [Fact]
    public void A_voice_part_cannot_be_drafted_by_hand()
    {
        var draft = SpecialistScaffold.Draft("style.work.voice");

        draft.Failed.Should().BeTrue();
        draft.Error.Should().Contain("loadout voice learn");
    }

    // -------------------------------------------------------- default style

    [Fact]
    public async Task The_default_style_is_in_force_where_a_project_chooses_none()
    {
        await SaveAsync(StyleLayer.Named, "work", Profile(Rule("plain", "Say it plainly.")));
        await RenderAsync(StyleLayer.Named, "work");

        var config = new LauncherConfig { InstructionContext = { DefaultStyle = "work" } };
        var service = new InstructionService(
            new SpecialistLibrary(), new SpecialistResolver(), new RepositoryEvidenceReader(), new StubConfig(config));

        (await service.DefaultStyleAsync()).Should().Be("work");

        var none = new ProjectManifest { Slug = Slug, Name = "Demo" };
        var chosen = new ProjectManifest { Slug = Slug, Name = "Demo", Specialists = { Style = "loose" } };

        var defaulted = await service.ResolveAsync(new InstructionRequest(Manifest: none, WorkspacePath: Workspace));

        defaulted.Value!.Selected.Select(s => s.Specialist.Id).Should().Contain("style.work.voice");

        var overridden = await service.ResolveAsync(new InstructionRequest(Manifest: chosen, WorkspacePath: Workspace));

        overridden.Value!.Selected.Select(s => s.Specialist.Id)
            .Should().NotContain("style.work.voice", "a project's own choice wins over the default");

        var outside = await service.ResolveAsync(new InstructionRequest(WorkspacePath: Workspace));

        outside.Value!.Selected.Select(s => s.Specialist.Id).Should().Contain("style.work.voice",
            "outside a project the default is all there is");
    }

    [Fact]
    public void The_commands_read_the_default_the_way_a_launch_does()
    {
        var none = new ProjectManifest { Slug = Slug };
        var chosen = new ProjectManifest { Slug = Slug, Specialists = { Style = "loose" } };

        StyleChoice.Of(none, null, "work").Should().Be("work");
        StyleChoice.Of(null, null, "work").Should().Be("work");
        StyleChoice.Of(chosen, null, "work").Should().Be("loose");
        StyleChoice.Of(none, null, "  ").Should().BeNull();
    }

    [Fact]
    public void The_default_style_is_a_setting_written_the_way_a_style_is_named()
    {
        var key = ConfigKeys.Find("default-style")!;
        var config = new LauncherConfig();

        key.Write(config, new MachineConfig(), " Work ");

        config.InstructionContext.DefaultStyle.Should().Be("work");
        key.IsMachineLocal.Should().BeFalse("a style is about the person, wherever they work");
    }

    // ------------------------------------------------------------ docs audit

    [Fact]
    public async Task The_docs_audit_checks_each_page_against_the_rules_for_docs()
    {
        var repository = Path.Combine(_root, "repo");
        Directory.CreateDirectory(Path.Combine(repository, "docs"));
        await File.WriteAllTextAsync(Path.Combine(repository, "README.md"), "# Widget\n\nThe color is red.\n");
        await File.WriteAllTextAsync(Path.Combine(repository, "docs", "guide.md"), "# Guide\n\nThe colour is red.\n");

        var british = new VoiceRule
        {
            Id = "british",
            Statement = "Write British spellings.",
            Check = new VoiceCheck { Type = VoiceChecker.PreferSpelling, Spellings = { ["color"] = "colour" } },
        };
        var commitsOnly = new VoiceRule
        {
            Id = "no-color",
            Statement = "Never say color in a commit.",
            Surfaces = [VoiceGuides.Commit],
            Check = new VoiceCheck { Type = VoiceChecker.AvoidPhrase, Phrases = ["colour"] },
        };

        var findings = VoiceChecker.Audit(repository, ["README.md", "docs/guide.md", "missing.md"], [british, commitsOnly]);

        findings.Should().ContainSingle();
        findings[0].Should().Match<DocsFinding>(f => f.Path == "README.md" && f.Line == 3 && f.Kind == "voice");
        findings[0].Detail.Should().StartWith("british: ");

        VoiceChecker.Audit(repository, ["README.md"], []).Should().BeEmpty();
    }

    private sealed class StubConfig(LauncherConfig config) : IConfigurationService
    {
        public Task<OperationResult<LauncherConfig>> LoadConfigAsync(CancellationToken ct = default) =>
            Task.FromResult(OperationResult<LauncherConfig>.Ok(config));

        public Task<OperationResult> SaveConfigAsync(LauncherConfig changed, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<OperationResult<MachineConfig>> LoadMachineAsync(CancellationToken ct = default) =>
            Task.FromResult(OperationResult<MachineConfig>.Ok(new MachineConfig()));

        public Task<OperationResult> SaveMachineAsync(MachineConfig machine, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<OperationResult<LauncherConfig>> UpdateConfigAsync(Action<LauncherConfig> change, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<OperationResult<MachineConfig>> UpdateMachineAsync(Action<MachineConfig> change, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
