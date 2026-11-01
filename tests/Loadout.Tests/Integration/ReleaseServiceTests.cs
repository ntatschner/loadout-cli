using System.Diagnostics;
using FluentAssertions;
using Loadout.Core.Delivery;
using Loadout.Core.Git;
using Loadout.Models.Projects;
using Loadout.Platform.Common;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// Releasing a project: the version and notes proposed, what stops a release,
/// and tag-push against a real repository whose origin is a bare repository
/// beside it, so nothing leaves the machine.
/// </summary>
public sealed class ReleaseServiceTests : IDisposable
{
    private readonly TemporaryWorkspace _space = new();

    public void Dispose() => _space.Dispose();

    [Theory]
    [InlineData("1.2.3", new[] { "Teams: fix the stall", "Fix a typo (#7)" }, "1.2.4", "patch")]
    [InlineData("1.2.3", new[] { "Teams: fix the stall", "Studio: add a release button" }, "1.3.0", "minor")]
    [InlineData("1.2.3", new[] { "feat!: drop the old flag" }, "2.0.0", "major")]
    [InlineData("0.111.0", new[] { "Delivery: closed pull requests go back to pending (#287)" }, "0.112.0", "minor")]
    public void The_version_proposed_follows_what_the_commits_since_say(string last, string[] since, string version, string bump)
    {
        var (proposed, why) = ReleaseService.Propose(last, since);

        proposed.Should().Be(version);
        why.Should().StartWith(bump);
    }

    [Fact]
    public void Nothing_is_proposed_with_no_earlier_version_or_nothing_since()
    {
        ReleaseService.Propose(null, ["Add it"]).Version.Should().BeNull();
        ReleaseService.Propose("1.0.0", []).Version.Should().BeNull();
    }

    [Fact]
    public void Proposed_notes_name_the_pull_requests_on_a_line_starting_with_words()
    {
        var notes = ReleaseService.ProposeNotes(["Merge pull request #286 from o/x", "Teams: list it (#249)", "Fix it (#249)"]);

        notes.Split('\n').Should().Contain("Pull requests: #249 #286");
        notes.Split('\n').Should().NotContain(line => line.StartsWith('#'), "git drops lines starting with # from a tag's message");
    }

    [Theory]
    [InlineData("Directory.Build.props", "<Project><PropertyGroup><Version>0.111.0</Version></PropertyGroup></Project>", "", "0.111.0")]
    [InlineData("package.json", "{ \"name\": \"x\", \"version\": \"2.0.1\" }", "", "2.0.1")]
    [InlineData("VERSION.txt", "release 4.5.6\n", @"release (\S+)", "4.5.6")]
    [InlineData("VERSION.txt", "4.5.6", "", null)]
    public void A_version_is_read_from_its_file_and_written_back_in_place(string file, string text, string pattern, string? version)
    {
        ReleaseService.VersionIn(file, text, pattern).Should().Be(version);

        if (version is not null)
        {
            var bumped = ReleaseService.WithVersion(file, text, pattern, "9.9.9")!;
            ReleaseService.VersionIn(file, bumped, pattern).Should().Be("9.9.9");
            bumped.Replace("9.9.9", version, StringComparison.Ordinal).Should().Be(text, "only the version changes");
        }
    }

    [Fact]
    public void A_recipe_that_is_none_of_the_words_is_refused_and_none_set_is_none()
    {
        ReleaseService.RecipeOf(new ProjectRelease()).Value.Should().Be(ReleaseService.None);
        ReleaseService.RecipeOf(new ProjectRelease { Recipe = "Tag-Push" }).Value.Should().Be(ReleaseService.TagPush);
        ReleaseService.RecipeOf(new ProjectRelease { Recipe = "goreleaser" }).Error.Should().Contain("not a release recipe");
    }

    [Fact]
    public void A_release_is_started_on_each_host_with_exactly_these_arguments()
    {
        var github = ReleaseService.Dispatch("github", "release.yml", "main", "1.3.0")!.Value;
        github.Executable.Should().Be("gh");
        github.Arguments.Should().Equal("workflow", "run", "release.yml", "--ref", "main", "-f", "version=1.3.0");
        ReleaseService.Dispatch("gitlab", string.Empty, "main", "1.3.0")!.Value.Executable.Should().Be("glab");
        ReleaseService.Dispatch("azure-devops", "Release", "main", "1.3.0")!.Value.Executable.Should().Be("az");
        ReleaseService.Dispatch("gitlab", string.Empty, "main", "1.3.0")!.Value.Arguments.Should().Equal(
            "ci", "run", "--branch", "main", "--variables", "VERSION:1.3.0");
        ReleaseService.Dispatch("azure-devops", "Release", "main", "1.3.0")!.Value.Arguments.Should().Equal(
            "pipelines", "run", "--name", "Release", "--branch", "main", "--parameters", "version=1.3.0", "--output", "none");
        ReleaseService.Dispatch("local", "x", "main", "1.3.0").Should().BeNull("local git has nothing to start");
    }

    [Fact]
    public async Task Tag_push_proposes_the_next_version_then_bumps_tags_with_the_notes_and_pushes()
    {
        var (repository, origin) = await ProjectAsync();
        var releases = Releases();

        var plan = (await releases.PlanAsync("widget")).Value!;

        plan.Problems.Should().BeEmpty();
        plan.LastTag.Should().Be("v1.2.0");
        plan.FileVersion.Should().Be("1.2.0");
        plan.Proposed.Should().Be("1.3.0");
        plan.Since.Should().Equal("Widgets: add the blue one (#5)", "Fix the red one");
        releases.CannotCut(plan, "1.3.0", null).Should().Contain("--notes");
        releases.CannotCut(plan, "1.3", "Notes.").Should().Contain("not a version");
        releases.CannotCut(plan, "1.2.0", "Notes.").Should().Contain("already released");

        var cut = await releases.CutAsync(plan, "1.3.0", "Blue widgets.\n\nPull requests: #5");

        cut.Succeeded.Should().BeTrue(cut.Error);
        cut.Value!.Bumped.Should().BeTrue();
        (await Git(origin, "show", "v1.3.0:Directory.Build.props")).Should().Contain("<Version>1.3.0</Version>");
        (await Git(origin, "for-each-ref", "refs/tags/v1.3.0", "--format=%(contents:body)")).Should().Be("Blue widgets.\n\nPull requests: #5");
        (await Git(origin, "log", "-1", "--format=%s", "main")).Should().Be("Release 1.3.0");
        (await Git(repository, "status", "--porcelain")).Should().BeEmpty();
        ReleaseLog.Read(_space.Paths.Paths.State).Should().ContainSingle()
            .Which.Should().Match<ReleaseRecord>(one => one.Tag == "v1.3.0" && one.Project == "widget" && one.Recipe == "tag-push");
    }

    [Fact]
    public async Task A_tag_whose_commit_does_not_carry_the_version_is_taken_back_and_nothing_is_pushed()
    {
        var (repository, origin) = await ProjectAsync();

        // A clean filter that puts the old version back as the file is
        // committed: the working file says 1.3.0, the commit says 1.2.9.
        await Git(repository, "config", "filter.freeze.clean", "sed s/1.3.0/1.2.9/");
        await File.WriteAllTextAsync(Path.Combine(repository, ".gitattributes"), "Directory.Build.props filter=freeze\n");
        await Git(repository, "add", ".gitattributes");
        await Git(repository, "commit", "-q", "-m", "Add attributes");
        await Git(repository, "push", "-q", "origin", "main");
        var releases = Releases();
        var plan = (await releases.PlanAsync("widget")).Value!;

        var cut = await releases.CutAsync(plan, "1.3.0", "Blue widgets.");

        cut.Error.Should().Contain("does not say 1.3.0");
        (await Git(repository, "tag", "--list", "v1.3.0")).Should().BeEmpty("the tag was taken back");
        (await Git(origin, "tag", "--list", "v1.3.0")).Should().BeEmpty("and never pushed");
        (await Git(origin, "log", "-1", "--format=%s", "main")).Should().Be("Add attributes");
        ReleaseLog.Read(_space.Paths.Paths.State).Should().BeEmpty("nothing was released");
    }

    [Fact]
    public async Task A_checkout_with_changes_or_on_another_branch_or_no_recipe_stops_a_release()
    {
        var (repository, _) = await ProjectAsync();
        await File.WriteAllTextAsync(Path.Combine(repository, "README.md"), "changed\n");

        var plan = (await Releases().PlanAsync("widget")).Value!;

        plan.Problems.Should().ContainSingle().Which.Should().Contain("not committed");
        Releases().CannotCut(plan, "1.3.0", "Notes.").Should().Contain("not committed");

        await Git(repository, "checkout", "-q", "--", "README.md");
        await Git(repository, "checkout", "-q", "-b", "elsewhere");
        (await Releases().PlanAsync("widget")).Value!.Problems.Should().ContainSingle().Which.Should().Contain("not main");

        await Git(repository, "checkout", "-q", "main");
        await SaveAsync(repository, recipe: string.Empty);
        (await Releases().PlanAsync("widget")).Value!.Problems.Should().ContainSingle().Which.Should().Contain("no release recipe");
    }

    private ReleaseService Releases()
    {
        var git = new ProcessLauncher();
        var resolver = new StubResolver("git");

        return new ReleaseService(
            new FakeProjects("widget", _space.Scratch("widget")),
            _space.Workspace,
            new LocalSquash(git, resolver),
            new FixedHosts(new LocalRepositoryHost()),
            git,
            resolver,
            _space.Paths,
            TimeProvider.System);
    }

    private async Task SaveAsync(string repository, string recipe)
    {
        var manifest = new ProjectManifest { Slug = "widget", Name = "widget" };
        manifest.Delivery.Release.Recipe = recipe;
        (await _space.Workspace.WriteProjectAsync(manifest)).Succeeded.Should().BeTrue();
    }

    /// <summary>
    /// A project on main at 1.2.0, tagged v1.2.0, with two commits since,
    /// pushed to a bare origin beside it, and set to release with tag-push.
    /// </summary>
    private async Task<(string Repository, string Origin)> ProjectAsync()
    {
        var repository = _space.Scratch("widget");
        var origin = _space.Scratch("widget-origin.git");

        await Git(origin, "init", "-q", "--bare", "-b", "main");
        await Git(repository, "init", "-q", "-b", "main");
        await Git(repository, "config", "user.email", "t@example.com");
        await Git(repository, "config", "user.name", "T");
        await Git(repository, "remote", "add", "origin", origin);
        await File.WriteAllTextAsync(
            Path.Combine(repository, "Directory.Build.props"),
            "<Project>\n  <PropertyGroup>\n    <Version>1.2.0</Version>\n  </PropertyGroup>\n</Project>\n");
        await File.WriteAllTextAsync(Path.Combine(repository, "README.md"), "# Widget\n");
        await Git(repository, "add", "-A");
        await Git(repository, "commit", "-q", "-m", "Release 1.2.0");
        await Git(repository, "tag", "-a", "v1.2.0", "-m", "Release 1.2.0");
        await File.WriteAllTextAsync(Path.Combine(repository, "red.txt"), "red\n");
        await Git(repository, "add", "-A");
        await Git(repository, "commit", "-q", "-m", "Fix the red one");
        await File.WriteAllTextAsync(Path.Combine(repository, "blue.txt"), "blue\n");
        await Git(repository, "add", "-A");
        await Git(repository, "commit", "-q", "-m", "Widgets: add the blue one (#5)");
        await Git(repository, "push", "-q", "origin", "main", "v1.2.0");
        await Git(repository, "fetch", "-q", "origin");
        await SaveAsync(repository, ReleaseService.TagPush);

        return (repository, origin);
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
