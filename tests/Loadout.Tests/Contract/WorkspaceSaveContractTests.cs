using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// What <c>workspace save --project</c> counts, from outside the process.
/// </summary>
/// <remarks>
/// The workspace is shared by every session on the machine, so a save that
/// counted and committed everything pending took other sessions' unfinished
/// work in other projects with it. Scoped, it counts one project's files; the
/// count is what a dry run reports and what a script reads.
/// </remarks>
[Collection(ContractCollection.Name)]
public sealed class WorkspaceSaveContractTests
{
    [BuiltCliFact]
    public async Task A_scoped_dry_run_counts_only_that_projects_files()
    {
        using var loadout = new LoadoutProcess();

        var (project, workspace) = await PendingInTwoProjects(loadout);

        var run = await loadout.RunAsync(
            "workspace", "save", "--project", project, "--dry-run", "--json");

        run.ExitCode.Should().Be(0, run.StandardError);

        var json = run.Json();
        json.GetProperty("changes").GetInt32().Should().Be(1, "only the named project's file is in scope");
        json.GetProperty("committed").GetBoolean().Should().BeFalse();

        // And a dry run still changed nothing.
        (await Git(workspace, "status", "--porcelain")).Should().Contain("projects/other/");
    }

    [BuiltCliFact]
    public async Task An_unscoped_dry_run_still_counts_everything()
    {
        using var loadout = new LoadoutProcess();

        await PendingInTwoProjects(loadout);

        var run = await loadout.RunAsync("workspace", "save", "--dry-run", "--json");

        run.ExitCode.Should().Be(0, run.StandardError);

        // The other half: without --project the command is the explicit way to
        // save everything, and it keeps doing that.
        run.Json().GetProperty("changes").GetInt32().Should().Be(2);
    }

    /// <summary>
    /// Registers a project, makes the workspace a repository, and leaves one
    /// uncommitted file in that project and one in another.
    /// </summary>
    private static async Task<(string Project, string Workspace)> PendingInTwoProjects(LoadoutProcess loadout)
    {
        var repository = Path.Combine(loadout.Home, "a-repository");

        Directory.CreateDirectory(repository);

        await Git(repository, "init");
        await File.WriteAllTextAsync(Path.Combine(repository, "readme.md"), "x");
        await Git(repository, "add", "-A");
        await Git(repository, "-c", "user.email=t@example.com", "-c", "user.name=T", "commit", "-m", "first");

        var added = await loadout.RunAsync("project", "add", repository, "--json");

        added.ExitCode.Should().Be(0, "the rest of this test is about a project that exists: " + added.StandardError);

        var project = added.Json().GetProperty("id").GetString()!;

        // Wherever this platform keeps the workspace, the project's directory
        // in it is projects/<slug>, and the workspace is two levels above that.
        var projectDirectory = Directory
            .EnumerateDirectories(loadout.Home, project, SearchOption.AllDirectories)
            .Single(path => Path.GetFileName(Path.GetDirectoryName(path)) == "projects");

        var workspace = Path.GetDirectoryName(Path.GetDirectoryName(projectDirectory))!;

        await Git(workspace, "init");
        await Git(workspace, "add", "-A");
        await Git(workspace, "-c", "user.email=t@example.com", "-c", "user.name=T", "commit", "-m", "first");

        await File.WriteAllTextAsync(Path.Combine(projectDirectory, "note.md"), "Mine.");

        Directory.CreateDirectory(Path.Combine(workspace, "projects", "other"));
        await File.WriteAllTextAsync(Path.Combine(workspace, "projects", "other", "note.md"), "Theirs.");

        return (project, workspace);
    }

    private static async Task<string> Git(string directory, params string[] arguments)
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

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return output;
    }
}
