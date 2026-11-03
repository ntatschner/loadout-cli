using FluentAssertions;
using Loadout.Core.Capabilities;
using Loadout.Core.Instructions;
using Loadout.Core.Triggers;
using Loadout.Models.Capabilities;
using Loadout.Models.Instructions;
using Loadout.Models.Projects;
using Loadout.Models.Tasks;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// The checks specialists declare in frontmatter, read from real files through
/// the real library and run against a fixture repository.
/// </summary>
public sealed class DeclaredCheckProfileTests : IDisposable
{
    private readonly TemporaryWorkspace _space = new();
    private readonly string _root;

    public DeclaredCheckProfileTests()
    {
        _root = _space.Scratch("repository");
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => _space.Dispose();

    /// <summary>GitHub answering only what a test sets, and 404 to the rest.</summary>
    private sealed class Scripted : IGitHubReads
    {
        public Dictionary<string, GitHubReply> Answers { get; } = new(StringComparer.Ordinal);

        public Task<GitHubReply> GetAsync(TriggerCredential credential, string path, string? etag, CancellationToken ct) =>
            Task.FromResult(Answers.TryGetValue(path, out var reply) ? reply : new GitHubReply(404));
    }

    private CapabilityProfiles Profiles(IGitHubReads? github = null) => new(
        _space.Workspace,
        _space.Yaml,
        github ?? new Scripted(),
        _ => Task.FromResult(TriggerCredential.Of(TriggerCredential.GhToken, "t0ken")),
        TimeProvider.System,
        new SpecialistLibrary());

    private ProjectResolution Project(bool here = true) => new(
        new ProjectRegistryEntry { Slug = "website", Remote = "https://github.com/me/site.git" },
        here ? _root : null,
        null,
        0,
        false);

    private void Repository(string relative)
    {
        var path = Path.Combine(_root, relative);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
    }

    /// <summary>A specialist in the workspace's global layer, or a project's when one is named.</summary>
    private void Specialist(string name, string checks, string? project = null)
    {
        var folder = project is null
            ? Path.Combine(_space.Workspace.LocalPath, "global", "specialists")
            : Path.Combine(_space.Workspace.LocalPath, "projects", project, "specialists");

        Directory.CreateDirectory(folder);
        File.WriteAllText(
            Path.Combine(folder, $"{name}.md"),
            $"---\nid: function.{name}\nkind: function\ntitle: {name}\nsummary: A test.\nchecks:\n{checks}\n---\n\nGuidance.\n");
    }

    private const string CodeOwners =
        "  - id: code-owners\n    name: A code owners file\n    types: [ci-cd]\n    files: [.github/CODEOWNERS, docs/CODEOWNERS]";

    private async Task<CapabilityResult> CheckAsync(string id, CapabilityProfiles? profiles = null, bool askGitHub = false, bool here = true)
    {
        var profile = await (profiles ?? Profiles()).BuildAsync(Project(here), askGitHub);

        return profile.Checks.Single(check => check.Id == id);
    }

    [Fact]
    public async Task A_declared_check_finds_a_file_under_a_dotted_folder()
    {
        Specialist("owners", CodeOwners);
        Repository(".github/CODEOWNERS");

        var check = await CheckAsync("code-owners");

        check.State.Should().Be(CapabilityState.Present);
        check.Evidence.Should().Contain(".github/CODEOWNERS");
        check.Name.Should().Be("A code owners file");
        check.Types.Should().Equal(TaskType.CiCd);
        check.Declared.Should().BeTrue();
    }

    [Fact]
    public async Task A_declared_check_with_no_matching_file_is_a_gap()
    {
        Specialist("owners", CodeOwners);
        Repository("README.md");

        (await CheckAsync("code-owners")).State.Should().Be(CapabilityState.Gap);
    }

    [Fact]
    public async Task A_project_not_on_this_machine_is_unknown_never_a_gap()
    {
        Specialist("owners", CodeOwners);

        (await CheckAsync("code-owners", here: false)).State.Should().Be(CapabilityState.Unknown);
    }

    [Fact]
    public async Task Built_in_checks_are_not_marked_declared_and_carry_their_types()
    {
        var check = await CheckAsync(CapabilityChecks.Release);

        check.Declared.Should().BeFalse();
        check.Types.Should().Equal(TaskType.Release);
        check.Name.Should().NotBeEmpty();
    }

    [Fact]
    public async Task A_workspace_specialist_overrides_a_built_in_check_in_place()
    {
        Specialist("own-ci", "  - id: ci\n    name: CI on our own server\n    types: [feature]\n    files: [ci/pipeline.groovy]");
        Repository("ci/pipeline.groovy");

        var profile = await Profiles().BuildAsync(Project(), askGitHub: false);
        var check = profile.Checks[0];

        check.Id.Should().Be(CapabilityChecks.Ci, "an override keeps the built-in's place");
        profile.Checks.Count(one => one.Id == CapabilityChecks.Ci).Should().Be(1);
        check.State.Should().Be(CapabilityState.Present, "with no workflow, the built-in would have found a gap");
        check.Types.Should().Equal(TaskType.Feature);
        check.Declared.Should().BeTrue();
    }

    [Fact]
    public async Task An_override_naming_only_types_keeps_how_the_built_in_decides()
    {
        Specialist("own-ci", "  - id: ci\n    types: [docs]");

        var check = await CheckAsync(CapabilityChecks.Ci);

        check.State.Should().Be(CapabilityState.Gap);
        check.Evidence.Should().Contain("No workflow");
        check.Name.Should().NotBeEmpty();
        check.Types.Should().Equal(TaskType.Docs);
    }

    [Fact]
    public async Task Two_workspace_specialists_declaring_one_id_make_it_unknown_and_name_both()
    {
        Specialist("first", CodeOwners);
        Specialist("second", CodeOwners);
        Repository(".github/CODEOWNERS");

        var check = await CheckAsync("code-owners");

        check.State.Should().Be(CapabilityState.Unknown, "a file being there does not settle which of them is right");
        check.Evidence.Should().Contain("function.first").And.Contain("function.second");
        check.Types.Should().Equal(TaskType.CiCd);
    }

    [Fact]
    public async Task A_clash_over_a_built_in_id_makes_the_built_in_unknown_too()
    {
        Specialist("first", "  - id: ci\n    types: [docs]");
        Specialist("second", "  - id: ci\n    types: [feature]");

        var check = await CheckAsync(CapabilityChecks.Ci);

        check.State.Should().Be(CapabilityState.Unknown, "the built-in alone would have found a gap");
        check.Types.Should().BeEquivalentTo([TaskType.Docs, TaskType.Feature]);
    }

    [Fact]
    public async Task A_project_specialist_replacing_a_workspace_one_by_its_id_is_no_clash()
    {
        Specialist("owners", CodeOwners);
        Specialist("owners", CodeOwners, project: "website");
        Repository(".github/CODEOWNERS");

        (await CheckAsync("code-owners")).State.Should().Be(CapabilityState.Present);
    }

    [Fact]
    public async Task A_declared_check_reads_a_github_setting_the_profile_already_has()
    {
        Specialist("rel", "  - id: published\n    name: Published\n    types: [release]\n    github: releases");

        var github = new Scripted();
        github.Answers["repos/me/site"] = new GitHubReply(200, """{"default_branch":"main"}""");
        github.Answers["repos/me/site/releases?per_page=1"] = new GitHubReply(200, "[{}]");

        (await CheckAsync("published", Profiles(github), askGitHub: true)).State.Should().Be(CapabilityState.Present);
        (await CheckAsync("published")).State.Should().Be(CapabilityState.Unknown, "GitHub was not asked");
    }

    [Fact]
    public async Task A_check_that_cannot_be_used_is_left_out_and_reported()
    {
        Specialist("broken", "  - id: nothing\n    types: [docs]\n  - id: bad-type\n    types: [nonsense]\n    files: [a]");

        var profile = await Profiles().BuildAsync(Project(), askGitHub: false);
        var catalogue = await new SpecialistLibrary().LoadAsync(_space.Workspace.LocalPath, "website");

        profile.Checks.Select(check => check.Id).Should().NotContain(["nothing", "bad-type"]);
        catalogue.Findings.Where(finding => finding.Kind == "specialist-check").Should().HaveCount(2);
    }

    [Theory]
    [InlineData("both", "  - id: both\n    types: [docs]\n    files: [a]\n    github: releases")]
    [InlineData("setting", "  - id: setting\n    types: [docs]\n    github: wikis")]
    [InlineData("untyped", "  - id: untyped\n    files: [a]")]
    public async Task Each_way_of_writing_a_check_wrongly_is_reported(string name, string checks)
    {
        Specialist(name, checks);

        var catalogue = await new SpecialistLibrary().LoadAsync(_space.Workspace.LocalPath, "website");

        catalogue.Findings.Should().ContainSingle(finding => finding.Kind == "specialist-check")
            .Which.Severity.Should().Be(RuleFindingSeverity.Warning);
    }

    [Fact]
    public async Task A_kept_profile_keeps_what_a_declared_check_found()
    {
        Specialist("owners", CodeOwners);

        var profiles = Profiles();

        (await profiles.KeepAsync(await profiles.BuildAsync(Project(), askGitHub: false))).Succeeded.Should().BeTrue();

        var kept = (await profiles.CachedAsync("website"))!.Checks.Single(check => check.Id == "code-owners");

        kept.Declared.Should().BeTrue();
        kept.Types.Should().Equal(TaskType.CiCd);
        kept.Name.Should().Be("A code owners file");
    }

    [Fact]
    public void A_file_not_seen_in_a_walk_that_stopped_short_is_unknown_never_a_gap()
    {
        var check = new DeclaredCheck("code-owners", "Owners", [TaskType.CiCd], ["CODEOWNERS"], string.Empty, null);

        CapabilityProfiles.FromFiles(check, (["README.md"], false)).State.Should().Be(CapabilityState.Unknown);
        CapabilityProfiles.FromFiles(check, (["README.md"], true)).State.Should().Be(CapabilityState.Gap);
        CapabilityProfiles.FromFiles(check, (["CODEOWNERS"], false)).State.Should().Be(CapabilityState.Present, "a file seen is seen, however far the walk got");
    }

    [Fact]
    public void A_walk_stopped_short_says_it_is_not_complete()
    {
        Repository("a.txt");
        Repository("b.txt");

        LocalProbes.Files(_root, most: 1).Complete.Should().BeFalse();
        LocalProbes.Files(_root).Complete.Should().BeTrue();
    }

    [Fact]
    public void A_walk_skips_machinery_but_not_ordinary_build_folders()
    {
        Repository("build/script.sh");
        Repository("node_modules/left/out.js");

        var files = LocalProbes.Files(_root).Files;

        files.Should().Contain("build/script.sh");
        files.Should().NotContain("node_modules/left/out.js");
    }

    [Theory]
    [InlineData(".github/CODEOWNERS", ".github/CODEOWNERS", true)]
    [InlineData("./.github/CODEOWNERS", ".github/CODEOWNERS", true)]
    [InlineData(".github/CODEOWNERS", "github/CODEOWNERS", false)]
    [InlineData(".editorconfig", ".editorconfig", true)]
    [InlineData("**/Dockerfile", "src/api/Dockerfile", true)]
    [InlineData("*.md", "docs/a.md", false)]
    public void Globs_read_from_the_repository_root(string glob, string path, bool matches) =>
        DeclaredChecks.Matches(glob, path).Should().Be(matches);
}
