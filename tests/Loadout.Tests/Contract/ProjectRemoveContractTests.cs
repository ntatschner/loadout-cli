using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// Removing a project, judged by whether it is still on the list afterwards.
/// </summary>
/// <remarks>
/// The list is read from the shared registry. Without <c>--from-workspace</c>
/// the command forgets only this machine's record, so the project stays listed
/// as not on this machine — and the command used to report that as "Removed",
/// which is what made it look as though nothing could remove a project.
/// </remarks>
[Collection(ContractCollection.Name)]
public sealed class ProjectRemoveContractTests
{
    [BuiltCliFact]
    public async Task Removing_only_here_says_the_project_is_still_listed_and_how_to_remove_it()
    {
        using var loadout = new LoadoutProcess();

        var project = await Registered(loadout);

        var run = await loadout.RunAsync("project", "remove", project, "--non-interactive");

        run.ExitCode.Should().Be(0, run.StandardError);

        var said = Unwrapped(run.StandardOutput);

        said.Should().NotContain("Removed", "the project has not left the list");
        said.Should().Contain("still in the shared registry");
        said.Should().Contain($"loadout project remove {project} --from-workspace");

        (await Listed(loadout)).Should().Contain(project, "which is what the message now says");
    }

    [BuiltCliFact]
    public async Task Removing_from_the_workspace_takes_it_off_the_list()
    {
        using var loadout = new LoadoutProcess();

        var project = await Registered(loadout);

        // The command the launcher's Delete key hands back, spelled the same.
        var run = await loadout.RunAsync(
            "project", "remove", project, "--from-workspace", "--non-interactive");

        run.ExitCode.Should().Be(0, run.StandardError);

        (await Listed(loadout)).Should().NotContain(project);
    }

    private static async Task<IReadOnlyList<string>> Listed(LoadoutProcess loadout)
    {
        var list = await loadout.RunAsync("project", "list", "--json");

        list.ExitCode.Should().Be(0, list.StandardError);

        return [.. list.Json().GetProperty("projects").EnumerateArray()
            .Select(p => p.GetProperty("id").GetString()!)];
    }

    /// <summary>The output with the console's line wrapping taken back out.</summary>
    private static string Unwrapped(string output) =>
        Regex.Replace(output, @"\s+", " ");

    /// <summary>Registers a repository, so the project is on this machine to begin with.</summary>
    private static async Task<string> Registered(LoadoutProcess loadout)
    {
        var repository = Path.Combine(loadout.Home, "a-repository");

        Directory.CreateDirectory(repository);

        await Git(repository, "init");
        await File.WriteAllTextAsync(Path.Combine(repository, "readme.md"), "x");
        await Git(repository, "add", "-A");
        await Git(repository, "-c", "user.email=t@example.com", "-c", "user.name=T",
            "commit", "-m", "first");

        var added = await loadout.RunAsync("project", "add", repository, "--json");

        added.ExitCode.Should().Be(0,
            "the rest of this test is about a project that exists: " + added.StandardError);

        return added.Json().GetProperty("id").GetString()!;
    }

    private static async Task Git(string directory, params string[] arguments)
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
    }
}
