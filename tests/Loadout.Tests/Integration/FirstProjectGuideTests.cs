using FluentAssertions;
using Loadout.Core.Git;
using Loadout.Core.Starters;
using Loadout.Platform.Common;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// What the guide does after each agent session: keeping the plan, and showing
/// what the build changed. Real Git, because what is committed is the point.
/// </summary>
public sealed class FirstProjectGuideTests : IAsyncLifetime
{
    private readonly TemporaryWorkspace _workspace = new();
    private readonly ThrottledProcessLauncher _processes = new();

    private GitManager _git = null!;
    private FirstProjectGuide _guide = null!;
    private FirstProjectProgress _progress = null!;

    public async Task InitializeAsync()
    {
        _git = new GitManager(_processes, new ExecutableResolver(
            new FakeEnvironmentProvider(_workspace.Scratch("home"), new Dictionary<string, string>())
            {
                PathDirectories = Environment.GetEnvironmentVariable("PATH")?
                    .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [],
                ExecutableExtensions = OperatingSystem.IsWindows() ? [".exe", ".cmd"] : [string.Empty],
            },
            []));

        // Only Git, the state directory and the clock are used by what these
        // tests call. Creating and protecting are covered by the contract
        // tests, through the real command.
        _guide = new FirstProjectGuide(
            templates: null!,
            projects: null!,
            policies: null!,
            tasks: null!,
            _git,
            _workspace.Workspace,
            _workspace.Paths,
            _workspace.Yaml,
            TimeProvider.System);

        var repository = _workspace.Scratch("first");

        await GitAsync(repository, "init", "-b", "main");
        await GitAsync(repository, "config", "user.email", "tests@example.invalid");
        await GitAsync(repository, "config", "user.name", "Guide Tests");
        await GitAsync(repository, "config", "core.excludesFile", string.Empty);

        await File.WriteAllTextAsync(Path.Combine(repository, "index.html"), "<p>start</p>\n");
        await GitAsync(repository, "add", "-A");
        await GitAsync(repository, "commit", "-m", "starter");

        var head = await _git.ResolveAsync(repository, "HEAD");

        _progress = new FirstProjectProgress
        {
            Starter = "web-page",
            Name = "first",
            Slug = "first",
            Path = repository,
            StarterCommit = head.Value,
            Done = [nameof(GuideStep.Create), nameof(GuideStep.Protect)],
        };
    }

    public Task DisposeAsync()
    {
        _workspace.Dispose();

        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_session_that_wrote_no_plan_leaves_the_step_to_be_done_again()
    {
        var kept = await _guide.KeepPlanAsync(_progress);

        kept.Succeeded.Should().BeTrue(kept.Error);
        kept.Value!.Written.Should().BeFalse();
        _progress.Next.Should().Be(GuideStep.Plan);
    }

    [Fact]
    public async Task The_plan_is_committed_on_its_own_and_anything_else_is_reported_not_taken()
    {
        await File.WriteAllTextAsync(Path.Combine(_progress.Path, FirstProjectBriefs.PlanFile), "# Plan\n");
        await File.WriteAllTextAsync(Path.Combine(_progress.Path, "index.html"), "<p>changed</p>\n");

        var kept = await _guide.KeepPlanAsync(_progress);

        kept.Succeeded.Should().BeTrue(kept.Error);
        kept.Value!.Written.Should().BeTrue();
        kept.Value.AlsoChanged.Should().ContainSingle().Which.Should().Be("index.html");

        var committed = await _git.ListCommitFilesAsync(_progress.Path, "HEAD");

        committed.Value!.Select(c => c.Path).Should().Equal(FirstProjectBriefs.PlanFile);

        (await _git.ListChangedFilesAsync(_progress.Path)).Value!
            .Should().Equal(["index.html"], "the planning session's stray change is the person's to keep or undo");

        _progress.Next.Should().Be(GuideStep.Build);
        _progress.PlanCommit.Should().NotBe(_progress.StarterCommit);
    }

    [Fact]
    public async Task Progress_kept_after_the_plan_is_what_a_second_run_reads()
    {
        await File.WriteAllTextAsync(Path.Combine(_progress.Path, FirstProjectBriefs.PlanFile), "# Plan\n");

        await _guide.KeepPlanAsync(_progress);

        var loaded = await _guide.LoadAsync();

        loaded.Value!.Next.Should().Be(GuideStep.Build);
        loaded.Value.PlanCommit.Should().Be(_progress.PlanCommit);
    }

    [Fact]
    public async Task The_review_shows_what_was_saved_since_the_plan_and_what_was_not()
    {
        await File.WriteAllTextAsync(Path.Combine(_progress.Path, FirstProjectBriefs.PlanFile), "# Plan\n");
        await _guide.KeepPlanAsync(_progress);

        // What a building session leaves: one change it committed, one it did not.
        await File.WriteAllTextAsync(Path.Combine(_progress.Path, "style.css"), "body {}\n");
        await GitAsync(_progress.Path, "add", "style.css");
        await GitAsync(_progress.Path, "commit", "-m", "style");
        await File.WriteAllTextAsync(Path.Combine(_progress.Path, "index.html"), "<p>built</p>\n");

        var review = await _guide.ReviewAsync(_progress);

        review.Succeeded.Should().BeTrue(review.Error);
        review.Value!.Saved.Should().Contain("style.css").And.NotContain(FirstProjectBriefs.PlanFile);
        review.Value.Unsaved.Should().Equal("index.html");
        review.Value.Page.Should().Be(Path.Combine(_progress.Path, "index.html"));

        var kept = await _guide.KeepBuildAsync(_progress);

        kept.Value.Should().BeTrue();
        (await _git.ListChangedFilesAsync(_progress.Path)).Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Progress_whose_folder_has_gone_is_stale()
    {
        var gone = new FirstProjectProgress { Slug = "gone", Path = Path.Combine(_progress.Path, "nowhere") };

        (await _guide.StaleReasonAsync(gone)).Should().Contain("no longer there");
    }

    private static async Task GitAsync(string directory, params string[] arguments)
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

        await process.WaitForExitAsync();

        process.ExitCode.Should().Be(0, $"git {string.Join(' ', arguments)}: {await process.StandardError.ReadToEndAsync()}");
    }
}
