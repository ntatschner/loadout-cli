using System.Diagnostics;
using FluentAssertions;
using Loadout.Core.Git;
using Loadout.Platform.Common;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// The head commit's time, asked of real git, for the status line's commit
/// segment.
/// </summary>
/// <remarks>
/// The commit is made with <c>GIT_COMMITTER_DATE</c> pinned, so the answer is
/// known exactly rather than being "about now".
/// </remarks>
public sealed class GitCommitTimeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "loadout-commit-time-" + Guid.NewGuid().ToString("N"));
    private readonly GitManager _git;

    public GitCommitTimeTests()
    {
        Directory.CreateDirectory(_root);

        _git = new GitManager(new ThrottledProcessLauncher(), new ExecutableResolver(new FakeEnvironmentProvider(_root, new Dictionary<string, string>())
        {
            PathDirectories = Environment.GetEnvironmentVariable("PATH")?
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [],
            ExecutableExtensions = OperatingSystem.IsWindows() ? [".exe", ".cmd"] : [string.Empty],
        }, []));

        Git(null, "init", "-b", "main");
        Git(null, "config", "user.email", "tests@example.invalid");
        Git(null, "config", "user.name", "Commit Time Tests");
    }

    public void Dispose()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A temporary directory left behind is not worth failing a test over.
        }
    }

    [Fact]
    public async Task A_commit_with_a_pinned_date_answers_exactly_that_time()
    {
        Git("2026-10-22T14:02:00+01:00", "commit", "--allow-empty", "--message", "pinned");

        var result = await _git.GetHeadCommitTimeAsync(_root);

        result.Succeeded.Should().BeTrue(result.Error);
        result.Value.Should().Be(new DateTimeOffset(2026, 10, 22, 14, 2, 0, TimeSpan.FromHours(1)));
    }

    [Fact]
    public async Task A_repository_with_no_commits_answers_none_and_does_not_fail()
    {
        var result = await _git.GetHeadCommitTimeAsync(_root);

        result.Succeeded.Should().BeTrue(result.Error);
        result.Value.Should().BeNull();
    }

    private void Git(string? committerDate, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = _root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        if (committerDate is not null)
        {
            start.Environment["GIT_COMMITTER_DATE"] = committerDate;
        }

        using var git = Process.Start(start)!;
        var error = git.StandardError.ReadToEndAsync();

        git.StandardOutput.ReadToEnd();
        git.WaitForExit();

        git.ExitCode.Should().Be(0, error.Result);
    }
}
