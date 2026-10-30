using FluentAssertions;
using Loadout.Core.Capabilities;
using Loadout.Core.Triggers;
using Loadout.Models.Capabilities;
using Loadout.Models.Projects;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// A project's capability profile, built from fixture repositories and a
/// GitHub that answers as scripted: it never claims a gap it has not seen.
/// </summary>
public sealed class CapabilityProfilesTests : IDisposable
{
    private const string Repo = "me/site";

    private readonly TemporaryWorkspace _space = new();
    private readonly string _root;
    private readonly Scripted _github = new();
    private TriggerCredential _token = TriggerCredential.Of(TriggerCredential.GhToken, "t0ken");

    public CapabilityProfilesTests()
    {
        _root = _space.Scratch("repository");
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => _space.Dispose();

    /// <summary>GitHub, answering each path as told, and every other with a 404.</summary>
    private sealed class Scripted : IGitHubReads
    {
        public Dictionary<string, GitHubReply> Answers { get; } = new(StringComparer.Ordinal);

        public List<string> Asked { get; } = [];

        public bool Hangs { get; set; }

        public async Task<GitHubReply> GetAsync(TriggerCredential credential, string path, string? etag, CancellationToken ct)
        {
            Asked.Add(path);

            if (Hangs)
            {
                await Task.Delay(Timeout.Infinite, ct);
            }

            return Answers.TryGetValue(path, out var reply) ? reply : new GitHubReply(404);
        }

        public void Readable(string branch = "main") =>
            Answers[$"repos/{Repo}"] = new GitHubReply(200, $$"""{"default_branch":"{{branch}}"}""");
    }

    private CapabilityProfiles Profiles() =>
        new(_space.Workspace, _space.Yaml, _github, _ => Task.FromResult(_token), TimeProvider.System);

    private ProjectResolution Project(string remote = "https://github.com/me/site.git", bool here = true) =>
        new(new ProjectRegistryEntry { Slug = "website", Remote = remote }, here ? _root : null, null, 0, false);

    private static CapabilityResult Check(CapabilityProfile profile, string id) => profile.Checks.Single(check => check.Id == id);

    private void Write(string relative, string text)
    {
        var path = Path.Combine(_root, relative);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Fact]
    public async Task Without_being_asked_to_GitHub_is_not_called_and_its_checks_are_unknown()
    {
        var profile = await Profiles().BuildAsync(Project(), askGitHub: false);

        _github.Asked.Should().BeEmpty();
        profile.GitHub.Should().Be(CapabilityProfiles.NotAsked);
        Check(profile, CapabilityChecks.Ci).State.Should().Be(CapabilityState.Gap, "no workflow is something the files show");
        Check(profile, CapabilityChecks.Release).State.Should().Be(CapabilityState.Unknown, "only GitHub can say whether releases are published by hand");
        Check(profile, CapabilityChecks.Environments).State.Should().Be(CapabilityState.Unknown);
        Check(profile, CapabilityChecks.BranchProtection).State.Should().Be(CapabilityState.Unknown);
    }

    [Fact]
    public async Task Ci_and_release_are_read_from_the_workflows()
    {
        Write(".github/workflows/ci.yml", "on: pull_request\njobs:\n  b:\n    steps: []\n");
        Write(".github/workflows/release.yml", "on:\n  push:\n    tags: ['v*']\njobs:\n  r:\n    uses: org/shared/.github/workflows/release.yml@v2\n");

        var profile = await Profiles().BuildAsync(Project(), askGitHub: false);

        Check(profile, CapabilityChecks.Ci).State.Should().Be(CapabilityState.Present);
        Check(profile, CapabilityChecks.Ci).Evidence.Should().Contain("ci.yml");
        Check(profile, CapabilityChecks.Release).State.Should().Be(CapabilityState.Delegated);
        Check(profile, CapabilityChecks.Release).Evidence.Should().Contain("org/shared/.github/workflows/release.yml@v2");
    }

    [Fact]
    public async Task The_release_evidence_names_the_workflow_named_for_releasing()
    {
        Write(".github/workflows/homebrew.yml", "on:\n  release:\n    types: [published]\njobs:\n  b:\n    steps: []\n");
        Write(".github/workflows/release.yml", "on:\n  push:\n    tags: ['v*']\njobs:\n  b:\n    steps: []\n");

        var profile = await Profiles().BuildAsync(Project(), askGitHub: false);

        Check(profile, CapabilityChecks.Release).Evidence.Should().Be("release.yml releases.");
    }

    [Fact]
    public async Task What_GitHub_has_is_present_and_what_it_lacks_is_a_gap()
    {
        _github.Readable();
        _github.Answers[$"repos/{Repo}/environments"] = new GitHubReply(200, """{"total_count":2}""");
        _github.Answers[$"repos/{Repo}/rules/branches/main"] = new GitHubReply(200, "[]");
        _github.Answers[$"repos/{Repo}/releases?per_page=1"] = new GitHubReply(200, "[]");

        var profile = await Profiles().BuildAsync(Project(), askGitHub: true);

        profile.GitHub.Should().Be(TriggerCredential.GhToken);
        Check(profile, CapabilityChecks.Environments).State.Should().Be(CapabilityState.Present);
        Check(profile, CapabilityChecks.BranchProtection).State.Should().Be(CapabilityState.Gap, "the repository was readable and main has nothing protecting it");
        Check(profile, CapabilityChecks.Release).State.Should().Be(CapabilityState.Gap, "no workflow and nothing published");
    }

    [Fact]
    public async Task Published_releases_with_no_workflow_are_a_manual_release()
    {
        _github.Readable();
        _github.Answers[$"repos/{Repo}/releases?per_page=1"] = new GitHubReply(200, """[{"id":1}]""");

        var profile = await Profiles().BuildAsync(Project(), askGitHub: true);

        Check(profile, CapabilityChecks.Release).State.Should().Be(CapabilityState.ManualRelease);
    }

    [Fact]
    public async Task A_ruleset_on_the_default_branch_is_protection()
    {
        _github.Readable("trunk");
        _github.Answers[$"repos/{Repo}/rules/branches/trunk"] = new GitHubReply(200, """[{"type":"pull_request"}]""");

        var profile = await Profiles().BuildAsync(Project(), askGitHub: true);

        Check(profile, CapabilityChecks.BranchProtection).State.Should().Be(CapabilityState.Present);
        Check(profile, CapabilityChecks.BranchProtection).Evidence.Should().Contain("trunk");
    }

    [Fact]
    public async Task A_refusal_is_no_permission_and_names_what_it_wanted()
    {
        _github.Readable();
        _github.Answers[$"repos/{Repo}/environments"] = new GitHubReply(403);
        _github.Answers[$"repos/{Repo}/branches/main/protection"] = new GitHubReply(403);

        var profile = await Profiles().BuildAsync(Project(), askGitHub: true);

        Check(profile, CapabilityChecks.Environments).State.Should().Be(CapabilityState.NoPermission);
        Check(profile, CapabilityChecks.Environments).Scope.Should().Be("Environments: read");
        Check(profile, CapabilityChecks.BranchProtection).State.Should().Be(CapabilityState.NoPermission);
        Check(profile, CapabilityChecks.BranchProtection).Scope.Should().Be("Administration: read");
    }

    [Fact]
    public async Task A_repository_the_token_cannot_read_leaves_every_GitHub_check_without_a_gap()
    {
        _github.Answers[$"repos/{Repo}"] = new GitHubReply(404);

        var profile = await Profiles().BuildAsync(Project(), askGitHub: true);

        foreach (var id in new[] { CapabilityChecks.Environments, CapabilityChecks.BranchProtection })
        {
            Check(profile, id).State.Should().Be(CapabilityState.NoPermission);
        }

        Check(profile, CapabilityChecks.Release).State.Should().Be(CapabilityState.NoPermission);
        profile.Checks.Should().NotContain(check => check.Id != CapabilityChecks.Ci && check.State == CapabilityState.Gap);
    }

    [Fact]
    public async Task GitHub_that_does_not_answer_in_time_is_unknown()
    {
        _github.Hangs = true;

        var profile = await Profiles().BuildAsync(Project(), askGitHub: true);

        Check(profile, CapabilityChecks.Environments).State.Should().Be(CapabilityState.Unknown);
        Check(profile, CapabilityChecks.Environments).Evidence.Should().Contain("did not answer within 10 seconds");
        _github.Asked.Should().ContainSingle("nothing else is asked once GitHub could not read the repository");
    }

    [Fact]
    public async Task No_token_is_unknown_and_says_how_to_get_one()
    {
        _token = TriggerCredential.Missing;

        var profile = await Profiles().BuildAsync(Project(), askGitHub: true);

        _github.Asked.Should().BeEmpty();
        profile.GitHub.Should().Be(CapabilityProfiles.NoToken);
        Check(profile, CapabilityChecks.Environments).Evidence.Should().Contain("gh auth login").And.Contain("GITHUB_TOKEN");
    }

    [Fact]
    public async Task A_project_not_on_github_or_not_on_this_machine_claims_nothing_it_could_not_see()
    {
        var elsewhere = await Profiles().BuildAsync(Project("https://gitlab.com/me/site.git"), askGitHub: true);
        var absent = await Profiles().BuildAsync(Project(here: false), askGitHub: false);

        elsewhere.GitHub.Should().Be(CapabilityProfiles.NotOnGitHub);
        _github.Asked.Should().BeEmpty();
        absent.Checks.Should().OnlyContain(check => check.State == CapabilityState.Unknown);
    }

    [Fact]
    public async Task A_kept_profile_reads_back_as_it_was_and_keeps_no_token()
    {
        Write("package.json", "{}");
        _github.Readable();
        var profiles = Profiles();
        var built = await profiles.BuildAsync(Project(), askGitHub: true);

        (await profiles.KeepAsync(built)).Succeeded.Should().BeTrue();

        var read = await profiles.CachedAsync("website");
        read.Should().BeEquivalentTo(built);
        File.ReadAllText(CapabilityProfiles.PathFor(_space.Workspace.LocalPath, "website")).Should().NotContain("t0ken");
    }
}
