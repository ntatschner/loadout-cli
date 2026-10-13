using FluentAssertions;
using Loadout.Core.Git;
using Loadout.Core.Voice;
using Loadout.Models.Voice;
using Loadout.Platform.Abstractions;
using Loadout.Platform.Common;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>Marking commits, against real git.</summary>
public sealed class VoiceCommitMarkTests : IAsyncLifetime
{
    private readonly ThrottledProcessLauncher _processes = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "loadout-voice-git-" + Guid.NewGuid().ToString("N"));

    private GitManager _git = null!;
    private string _repository = null!;

    public async Task InitializeAsync()
    {
        _git = new GitManager(_processes, new ExecutableResolver(new FakeEnvironmentProvider(_root, new Dictionary<string, string>())
        {
            PathDirectories = Environment.GetEnvironmentVariable("PATH")?
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [],
            ExecutableExtensions = OperatingSystem.IsWindows() ? [".exe", ".cmd"] : [string.Empty],
        }, []));

        _repository = Path.Combine(_root, "repo");
        Directory.CreateDirectory(_repository);

        await GitAsync("init", "-b", "main");
        await GitAsync("config", "user.email", "tests@example.invalid");
        await GitAsync("config", "user.name", "Voice Tests");
        await GitAsync("config", "core.excludesFile", string.Empty);

        await GitAsync("commit", "--allow-empty", "--message", "First subject");
        await GitAsync("commit", "--allow-empty", "--message", "Second subject\n\nA body, with a blank line above it.");
        await GitAsync("commit", "--allow-empty", "--message", "Third subject");
    }

    public Task DisposeAsync()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(_root, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return Task.CompletedTask;
    }

    private async Task<string> GitAsync(params string[] arguments)
    {
        var result = await _processes.RunAsync(
            new ProcessRequest("git", arguments, _repository), TimeSpan.FromSeconds(60));

        result.Succeeded.Should().BeTrue(result.Error);

        return result.Value!.StandardOutput;
    }

    [Fact]
    public async Task One_commit_is_read_alone_and_with_its_whole_message()
    {
        var read = await _git.ReadCommitMessagesAsync(_repository, "HEAD~1");

        read.Value.Should().ContainSingle("one commit is not its history")
            .Which.Message.Should().Be("Second subject\n\nA body, with a blank line above it.");
    }

    [Fact]
    public async Task A_range_is_every_commit_in_it_newest_first()
    {
        var read = await _git.ReadCommitMessagesAsync(_repository, "HEAD~2..HEAD");

        read.Value!.Select(c => c.Message.Split('\n')[0]).Should().Equal("Third subject", "Second subject");
        read.Value!.Should().OnlyContain(c => c.Sha.Length == 40);
    }

    [Fact]
    public async Task Something_that_is_not_a_commit_is_refused()
    {
        (await _git.ReadCommitMessagesAsync(_repository, "no-such-branch")).Failed.Should().BeTrue();
        (await _git.ReadCommitMessagesAsync(_repository, "--all")).Failed.Should().BeTrue("an option is not a revision");
    }

    [Fact]
    public async Task A_marked_commit_holds()
    {
        var store = new VoiceStore(Path.Combine(_root, "config"), Path.Combine(_root, "state"), null);
        var marks = new VoiceMarks(_git, new FakePullRequests(), store, TimeProvider.System);

        var candidates = (await marks.CandidatesAsync(_repository, "HEAD~2..HEAD")).Value!;
        candidates.Should().HaveCount(2).And.OnlyContain(c => c.Kind == VoiceMarkKind.Commit);

        await marks.MarkAsync(_repository, candidates);

        (await marks.StandingAsync(_repository)).Value!.Should().HaveCount(2)
            .And.OnlyContain(s => s.State == VoiceMarkState.Holds);
    }
}
