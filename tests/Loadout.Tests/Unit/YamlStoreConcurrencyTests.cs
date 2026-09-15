using FluentAssertions;
using Loadout.Core.Configuration;
using Loadout.Models.Configuration;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What happens when two launchers write the same file at the same moment.
/// </summary>
/// <remarks>
/// Not a stress test. Several sessions on one machine is the ordinary way this
/// is used — a status line runs on every prompt, a launch writes the ledger, a
/// person types 'config set' — and every one of them reaches the same handful
/// of files.
/// </remarks>
public sealed class YamlStoreConcurrencyTests
{
    private static string Scratch()
    {
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(directory);

        return directory;
    }

    [Fact]
    public async Task Writers_arriving_together_all_get_their_write()
    {
        var directory = Scratch();

        try
        {
            var path = Path.Combine(directory, "config.yaml");
            var store = new YamlStore(new NoOpFilePermissions());

            var results = await Task.WhenAll(Enumerable.Range(0, 24).Select(i => Task.Run(() =>
                store.SaveAsync(
                    path,
                    new LauncherConfig { DefaultAgent = $"agent-{i}" },
                    restrictPermissions: false))));

            // Before the lock, twenty-four writers sharing one temporary name
            // produced nineteen failures reading "the process cannot access the
            // file ... because it is being used by another process", and the
            // ones that did not fail were worse: a process could move a
            // temporary file another process had just filled, publishing
            // somebody else's content under its own write.
            //
            // With the lock, the only failure the store can legitimately
            // report here is a writer giving up after its five-second wait,
            // which is the store's own rule for a command line that must not
            // hang on a dead launcher's lock, and which twenty-four writers
            // queued on one loaded CI runner do reach: twice on 14 September
            // 2026, on machines busy with other legs of the suite. That is the
            // store working as designed on a slow machine, not the defect this
            // test exists for, so it is allowed and every other failure is not.
            var failures = results.Where(r => r.Failed).ToList();

            failures.Where(r => !r.Error!.Contains("holding it for longer than", StringComparison.Ordinal))
                .Should().BeEmpty(string.Join(" || ", failures.Select(r => r.Error)));

            results.Count(r => r.Succeeded).Should().BeGreaterThan(0,
                "at least the first writer through the lock must land its write");

            // And what landed is one whole write, not two halves of two.
            var read = await store.LoadAsync(path, () => new LauncherConfig());

            read.Succeeded.Should().BeTrue(read.Error);
            read.Value!.DefaultAgent.Should().MatchRegex(@"^agent-\d+$");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task A_change_made_at_the_same_moment_as_another_is_not_lost()
    {
        var directory = Scratch();

        try
        {
            var path = Path.Combine(directory, "config.yaml");
            var store = new YamlStore(new NoOpFilePermissions());

            const int Writers = 16;

            await Task.WhenAll(Enumerable.Range(0, Writers).Select(i => Task.Run(() =>
                store.UpdateAsync<LauncherConfig>(
                    path,
                    () => new LauncherConfig(),
                    config => config.Editor.Profiles[$"agent-{i}"] = $"profile-{i}",
                    restrictPermissions: false))));

            var read = await store.LoadAsync(path, () => new LauncherConfig());

            // The reason load-modify-save has to happen inside one lock. Done
            // separately, every writer reads the same starting file and writes
            // its own change over the others: the file stays valid, each
            // command reports success, and all but one of the settings is
            // simply not there.
            read.Value!.Editor.Profiles.Should().HaveCount(Writers);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Writing_a_file_leaves_nothing_beside_it()
    {
        var directory = Scratch();

        try
        {
            var path = Path.Combine(directory, "config.yaml");
            var store = new YamlStore(new NoOpFilePermissions());

            await store.SaveAsync(path, new LauncherConfig(), restrictPermissions: false);
            await store.UpdateAsync<LauncherConfig>(
                path,
                () => new LauncherConfig(),
                config => config.DefaultAgent = "claude",
                restrictPermissions: false);

            // The lock lived beside the file it guarded at first, which put one
            // into the workspace — a Git repository, so it would have been
            // committed — and one into the state directory, where restoring a
            // backup enumerates everything under its id and would have taken a
            // lock file for a payload. Nothing the store writes belongs
            // anywhere but at the path it was asked for.
            Directory.EnumerateFileSystemEntries(directory)
                .Select(Path.GetFileName)
                .Should().Equal("config.yaml");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
