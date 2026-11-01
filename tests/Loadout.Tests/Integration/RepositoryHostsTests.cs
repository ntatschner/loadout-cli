using System.Diagnostics;
using FluentAssertions;
using Loadout.Core.Git;
using Loadout.Platform.Common;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// Each host's exact arguments for open, ready, merge, close and state, how
/// its answers are read, how a project's host is chosen, and local git's
/// squash-merge against a real repository.
/// </summary>
/// <remarks>
/// The arguments are pinned here rather than read from each CLI, because a
/// dropped <c>--squash</c> or <c>--draft</c> is the mistake that matters and
/// none of these CLIs can be run in the suite.
/// </remarks>
public sealed class RepositoryHostsTests : IDisposable
{
    private static readonly PullRequestOpening Draft = new("teams/r1/implementer-2", "Fix the login", "Why it changed.", Draft: true);
    private static readonly PullRequestOpening ForReview = Draft with { Draft = false };

    private readonly StubProcessLauncher _stub = new(string.Empty);
    private readonly TemporaryWorkspace _space = new();

    public void Dispose() => _space.Dispose();

    private CliRepositoryHost Gh => new GhRepositoryHost(_stub, new StubResolver("gh"));

    private CliRepositoryHost Glab => new GlabRepositoryHost(_stub, new StubResolver("glab"));

    private CliRepositoryHost Az => new AzureRepositoryHost(_stub, new StubResolver("az"));

    private CliRepositoryHost Own => new CommandRepositoryHost(_stub, new StubResolver("deliver"), "deliver");

    [Fact]
    public void GitHub_is_asked_with_exactly_these_arguments()
    {
        const string pr = "https://github.com/o/website/pull/7";

        Gh.Opens(Draft).Should().Equal(
            "pr", "create", "--draft", "--head", "teams/r1/implementer-2", "--title", "Fix the login", "--body", "Why it changed.");
        Gh.Opens(ForReview).Should().NotContain("--draft");
        Gh.Readies(pr).Should().Equal("pr", "ready", pr);
        Gh.Merges(pr, "Fix the login", string.Empty).Should().Equal(
            "pr", "merge", pr, "--squash", "--auto", "--subject", "Fix the login", "--body", string.Empty);
        Gh.Closes(pr).Should().Equal("pr", "close", pr);
        Gh.States(pr).Should().Equal("pr", "view", pr, "--json", "state");
        Gh.SignsIn().Should().Equal("auth", "status", "--active");
    }

    [Fact]
    public void GitLab_is_asked_with_exactly_these_arguments_naming_the_merge_request_by_number()
    {
        const string mr = "https://gitlab.com/o/website/-/merge_requests/12";

        Glab.Opens(Draft).Should().Equal(
            "mr", "create", "--source-branch", "teams/r1/implementer-2", "--title", "Fix the login",
            "--description", "Why it changed.", "--draft", "--yes");
        Glab.Opens(ForReview).Should().NotContain("--draft");
        Glab.Readies(mr).Should().Equal("mr", "update", "12", "--ready");
        Glab.Merges(mr, "Fix the login", "Because.").Should().Equal(
            "mr", "merge", "12", "--squash", "--auto-merge", "--squash-message", "Fix the login\n\nBecause.", "--yes");
        Glab.Merges(mr, "Fix the login", string.Empty).Should().Contain("Fix the login").And.NotContain("Fix the login\n\n");
        Glab.Closes(mr).Should().Equal("mr", "close", "12");
        Glab.States(mr).Should().Equal("mr", "view", "12", "--output", "json");
        Glab.SignsIn().Should().Equal("auth", "status");
    }

    [Fact]
    public void Azure_DevOps_is_asked_with_exactly_these_arguments()
    {
        const string pr = "https://dev.azure.com/o/p/_git/website/pullrequest/42";

        Az.Opens(Draft).Should().Equal(
            "repos", "pr", "create", "--source-branch", "teams/r1/implementer-2", "--title", "Fix the login",
            "--description", "Why it changed.", "--draft", "true", "--output", "json");
        Az.Opens(ForReview).Should().ContainInOrder("--draft", "false");
        Az.Readies(pr).Should().Equal("repos", "pr", "update", "--id", "42", "--draft", "false", "--output", "none");
        Az.Merges(pr, "Fix the login", "Because.").Should().Equal(
            "repos", "pr", "update", "--id", "42", "--auto-complete", "true", "--squash", "true",
            "--delete-source-branch", "false", "--merge-commit-message", "Fix the login\n\nBecause.", "--output", "none");
        Az.Closes(pr).Should().Equal("repos", "pr", "update", "--id", "42", "--status", "abandoned", "--output", "none");
        Az.States(pr).Should().Equal("repos", "pr", "show", "--id", "42", "--output", "json");
        Az.SignsIn().Should().Equal("account", "show", "--output", "none");
    }

    [Fact]
    public void A_command_of_the_projects_own_is_asked_with_its_verbs()
    {
        const string pr = "https://git.example.com/pr/3";

        Own.Opens(Draft).Should().Equal(
            "open", "--head", "teams/r1/implementer-2", "--title", "Fix the login", "--body", "Why it changed.", "--draft");
        Own.Readies(pr).Should().Equal("ready", pr);
        Own.Merges(pr, "Fix", "Body").Should().Equal("merge", pr, "--subject", "Fix", "--body", "Body");
        Own.Closes(pr).Should().Equal("close", pr);
        Own.States(pr).Should().Equal("state", pr);
        Own.SignsIn().Should().Equal("check");
    }

    [Theory]
    [InlineData("gh", """{"state":"OPEN"}""", PullRequestState.Open)]
    [InlineData("gh", """{"state":"MERGED"}""", PullRequestState.Merged)]
    [InlineData("gh", """{"state":"CLOSED"}""", PullRequestState.Closed)]
    [InlineData("glab", """{"iid":12,"state":"opened"}""", PullRequestState.Open)]
    [InlineData("glab", """{"iid":12,"state":"merged"}""", PullRequestState.Merged)]
    [InlineData("glab", """{"iid":12,"state":"closed"}""", PullRequestState.Closed)]
    [InlineData("az", """{"pullRequestId":42,"status":"active"}""", PullRequestState.Open)]
    [InlineData("az", """{"pullRequestId":42,"status":"completed"}""", PullRequestState.Merged)]
    [InlineData("az", """{"pullRequestId":42,"status":"abandoned"}""", PullRequestState.Closed)]
    [InlineData("deliver", "merged\n", PullRequestState.Merged)]
    public void Each_hosts_answer_is_read_as_where_the_pull_request_stands(string host, string printed, PullRequestState state) =>
        Host(host).Reads(printed).Should().Be(state);

    [Theory]
    [InlineData("gh", """{"state":"QUEUED"}""")]
    [InlineData("glab", "not json")]
    [InlineData("az", """{"status":"notSet"}""")]
    public void An_answer_in_a_shape_not_known_is_not_read_as_anything(string host, string printed) =>
        Host(host).Reads(printed).Should().BeNull();

    [Fact]
    public async Task Azure_DevOps_gives_no_address_so_one_is_made_from_the_repository_and_number()
    {
        var stub = new StubProcessLauncher(
            """{"pullRequestId":42,"repository":{"webUrl":"https://dev.azure.com/o/p/_git/website"}}""");
        var az = new AzureRepositoryHost(stub, new StubResolver("az"));

        var opened = await az.OpenAsync(Here, Draft);

        opened.Value.Should().Be("https://dev.azure.com/o/p/_git/website/pullrequest/42");
    }

    [Fact]
    public async Task A_signed_out_host_names_how_to_sign_in_and_never_what_it_printed()
    {
        var stub = new StubProcessLauncher("Logged in to gitlab.com as someone (token glpat-abc)", exitCode: 1);

        var said = await new GlabRepositoryHost(stub, new StubResolver("glab")).UnavailableAsync(Here);

        said.Should().Contain("glab auth login").And.NotContain("glpat").And.NotContain("someone");
    }

    [Theory]
    [InlineData("https://github.com/o/website.git", RepositoryHosts.GitHub)]
    [InlineData("git@github.com:o/website.git", RepositoryHosts.GitHub)]
    [InlineData("https://gitlab.com/o/website.git", RepositoryHosts.GitLab)]
    [InlineData("git@gitlab.example.org:o/website.git", RepositoryHosts.GitLab)]
    [InlineData("https://o@dev.azure.com/o/p/_git/website", RepositoryHosts.AzureDevOps)]
    [InlineData("git@ssh.dev.azure.com:v3/o/p/website", RepositoryHosts.AzureDevOps)]
    [InlineData("https://o.visualstudio.com/p/_git/website", RepositoryHosts.AzureDevOps)]
    [InlineData(null, RepositoryHosts.Local)]
    [InlineData("", RepositoryHosts.Local)]
    [InlineData("https://git.example.com/o/website.git", null)]
    public void The_host_is_read_from_origins_address_and_an_unknown_one_is_not_guessed(string? remote, string? provider) =>
        RepositoryHosts.FromRemote(remote).Should().Be(provider);

    [Fact]
    public async Task A_projects_own_provider_wins_over_its_origin_and_a_word_that_is_none_is_refused()
    {
        await SaveAsync(manifest => manifest.Delivery.Provider = "gitlab");
        var hosts = Hosts("https://github.com/o/website.git");

        var chosen = await hosts.ForAsync("website", Here);

        chosen.Value!.Host.Provider.Should().Be(RepositoryHosts.GitLab);
        chosen.Value.From.Should().Contain("delivery.provider");

        await SaveAsync(manifest => manifest.Delivery.Provider = "bitbucket");

        (await hosts.ForAsync("website", Here)).Error.Should().Contain("not a delivery provider");
    }

    [Fact]
    public async Task An_origin_naming_no_known_host_asks_for_the_provider_to_be_set()
    {
        await SaveAsync(_ => { });

        var chosen = await Hosts("https://git.example.com/o/website.git").ForAsync("website", Here);

        chosen.Error.Should().Contain("delivery.provider");
    }

    [Fact]
    public async Task A_command_provider_with_no_command_is_refused()
    {
        await SaveAsync(manifest => manifest.Delivery.Provider = "command");

        (await Hosts(null).ForAsync("website", Here)).Error.Should().Contain("delivery.command");
    }

    [Fact]
    public async Task Local_git_squashes_a_branch_into_main_as_one_commit_with_the_message_given()
    {
        var repository = await RepositoryAsync();
        var squash = LocalSquash();

        var plan = await squash.PlanAsync(repository, "feature", "main");

        plan.Succeeded.Should().BeTrue(plan.Error);
        plan.Value!.DiffStat.Should().Contain("feature.txt");
        plan.Value.CheckedOutAt.Should().BeNull("main is not checked out anywhere");
        (await Git(repository, "rev-parse", "main")).Should().Be(plan.Value.TargetCommit, "working it out changes nothing");

        var made = await squash.ApplyAsync(repository, plan.Value, "Add the feature", "Two commits, one change.");

        made.Succeeded.Should().BeTrue(made.Error);
        (await Git(repository, "rev-parse", "main")).Should().Be(made.Value);
        (await Git(repository, "log", "-1", "--format=%B", "main")).Should().Be("Add the feature\n\nTwo commits, one change.");
        (await Git(repository, "rev-list", "--count", $"{plan.Value.TargetCommit}..main")).Should().Be("1", "squashed into one");
        (await Git(repository, "rev-parse", "main^{tree}")).Should().Be(plan.Value.Tree);
        (await Git(repository, "rev-parse", "feature")).Should().Be(plan.Value.BranchCommit, "the branch is left as it was");
    }

    [Fact]
    public async Task Local_git_fast_forwards_main_where_it_is_checked_out_and_refuses_when_that_tree_has_changes()
    {
        var repository = await RepositoryAsync();
        await Git(repository, "checkout", "-q", "main");
        var squash = LocalSquash();

        var plan = (await squash.PlanAsync(repository, "feature", "main")).Value!;
        plan.CheckedOutAt.Should().NotBeNull();

        await File.WriteAllTextAsync(Path.Combine(repository, "README.md"), "changed\n");
        var refused = await squash.ApplyAsync(repository, plan, "Add the feature", string.Empty);

        refused.Error.Should().Contain("not committed");
        (await Git(repository, "rev-parse", "main")).Should().Be(plan.TargetCommit);

        await Git(repository, "checkout", "-q", "--", "README.md");
        var made = await squash.ApplyAsync(repository, plan, "Add the feature", string.Empty);

        made.Succeeded.Should().BeTrue(made.Error);
        File.Exists(Path.Combine(repository, "feature.txt")).Should().BeTrue("the checked-out tree moved with main");
    }

    [Fact]
    public async Task Local_git_refuses_a_merge_with_conflicts_and_one_where_a_branch_moved_since_it_was_shown()
    {
        var repository = await RepositoryAsync();
        var squash = LocalSquash();
        var plan = (await squash.PlanAsync(repository, "feature", "main")).Value!;

        await Git(repository, "checkout", "-q", "main");
        await File.WriteAllTextAsync(Path.Combine(repository, "feature.txt"), "something else\n");
        await Git(repository, "add", "-A");
        await Git(repository, "commit", "-q", "-m", "clash");
        var mainNow = await Git(repository, "rev-parse", "main");

        (await squash.ApplyAsync(repository, plan, "Add the feature", string.Empty)).Error.Should().Contain("moved");
        (await Git(repository, "rev-parse", "main")).Should().Be(mainNow);

        (await squash.PlanAsync(repository, "feature", "main")).Error.Should().Contain("does not merge cleanly");
    }

    [Fact]
    public async Task Local_git_finds_main_to_merge_into_and_refuses_a_branch_already_in_it()
    {
        var repository = await RepositoryAsync();
        var squash = LocalSquash();

        (await squash.TargetAsync(repository)).Value.Should().Be("main");
        (await squash.PlanAsync(repository, "main", "main")).Failed.Should().BeTrue();

        await Git(repository, "branch", "old", "main");
        (await squash.PlanAsync(repository, "old", "main")).Error.Should().Contain("in main already");
    }

    private string Here => _space.Scratch("website");

    private async Task SaveAsync(Action<Loadout.Models.Projects.ProjectManifest> change)
    {
        var manifest = new Loadout.Models.Projects.ProjectManifest { Slug = "website", Name = "website" };
        change(manifest);
        (await _space.Workspace.WriteProjectAsync(manifest)).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task A_branch_says_how_far_behind_its_target_it_is_and_whether_it_is_in_it_already()
    {
        var repository = await RepositoryAsync();
        var squash = LocalSquash();

        (await squash.StandingAsync(repository, "feature", "main")).Should().Be(new BranchStanding("main", 0, false));

        await Git(repository, "checkout", "-q", "main");
        await File.WriteAllTextAsync(Path.Combine(repository, "other.txt"), "x\n");
        await Git(repository, "add", "-A");
        await Git(repository, "commit", "-q", "-m", "elsewhere");

        (await squash.StandingAsync(repository, "feature", "main")).Should().Be(new BranchStanding("main", 1, false));

        await Git(repository, "merge", "-q", "--no-edit", "feature");

        (await squash.StandingAsync(repository, "feature", "main"))!.Contained.Should().BeTrue("it was merged as it was");
        (await squash.StandingAsync(repository, "gone", "main")).Should().BeNull();
    }

    [Theory]
    [InlineData(0, false, "")]
    [InlineData(3, false, "[dim](3 behind main)[/]")]
    [InlineData(25, false, "[yellow](25 behind main)[/]")]
    [InlineData(4, true, "[yellow](already in main)[/]")]
    public void The_list_says_a_branch_is_behind_and_calls_it_out_once_it_is_well_behind(int behind, bool contained, string said) =>
        Loadout.Cli.Commands.PrListCommand.Standing(new BranchStanding("main", behind, contained)).Trim().Should().Be(said);

    private CliRepositoryHost Host(string name) => name switch
    {
        "gh" => Gh,
        "glab" => Glab,
        "az" => Az,
        _ => Own,
    };

    private RepositoryHosts Hosts(string? remote) =>
        new(_stub, new StubResolver("x"), _space.Workspace,
            new FakeGit(Here) { State = new GitRepositoryState(Here, "main", remote, true, "abc") });

    private static LocalSquash LocalSquash() => new(new ProcessLauncher(), new StubResolver("git"));

    /// <summary>A repository on main with a branch, feature, two commits ahead of it.</summary>
    private async Task<string> RepositoryAsync()
    {
        var repository = _space.Scratch("local-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(repository);
        await Git(repository, "init", "-q", "-b", "main");
        await Git(repository, "config", "user.email", "t@example.com");
        await Git(repository, "config", "user.name", "T");
        await File.WriteAllTextAsync(Path.Combine(repository, "README.md"), "# Website\n");
        await Git(repository, "add", "-A");
        await Git(repository, "commit", "-q", "-m", "first");
        await Git(repository, "checkout", "-q", "-b", "feature");
        await File.WriteAllTextAsync(Path.Combine(repository, "feature.txt"), "one\n");
        await Git(repository, "add", "-A");
        await Git(repository, "commit", "-q", "-m", "one");
        await File.AppendAllTextAsync(Path.Combine(repository, "feature.txt"), "two\n");
        await Git(repository, "commit", "-q", "-am", "two");

        return repository;
    }

    private static async Task<string> Git(string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        return output.Trim().Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
