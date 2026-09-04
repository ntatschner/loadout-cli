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

            // The temporary file used to be '<path>.tmp' for every writer,
            // which is a shared name and not a private scratch file. Twenty-four
            // of these produced nineteen failures reading "the process cannot
            // access the file ... because it is being used by another process",
            // and the ones that did not fail were worse: a process could move a
            // temporary file another process had just filled, publishing
            // somebody else's content under its own write.
            results.Where(r => r.Failed).Should().BeEmpty(
                string.Join(" || ", results.Where(r => r.Failed).Select(r => r.Error)));

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
