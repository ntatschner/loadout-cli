using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// Reading and changing what a project carries into every session.
/// </summary>
/// <remarks>
/// <para>
/// From outside the process, because the defect being fixed was that there was
/// no outside: both switches default to off, the only assignment anywhere was
/// at registration, and a project registered from a repository could be changed
/// only by editing the manifest in the workspace by hand. What matters is what
/// is on disk afterwards, which the command cannot be asked.
/// </para>
/// <para>
/// A repository is registered rather than assumed, so the project resolves as a
/// versioned one — which is the case that was stuck off, and the only case
/// worth testing.
/// </para>
/// </remarks>
[Collection(ContractCollection.Name)]
public sealed class ProjectContextContractTests
{
    [BuiltCliFact]
    public async Task Both_switches_are_reported_for_a_project()
    {
        using var loadout = new LoadoutProcess();

        var project = await Registered(loadout);

        var run = await loadout.RunAsync("project", "context", "--project", project, "--json");

        run.ExitCode.Should().Be(0, run.StandardError);

        var context = run.Json().GetProperty("context");

        // Both, and both off. A project registered from a repository gets
        // 'Tasks = !state.Versioned' and nothing else ever touched either
        // switch, so this is the state every such project starts in.
        context.GetProperty("tasks").GetBoolean().Should().BeFalse();
        context.GetProperty("code-map").GetBoolean().Should().BeFalse();
    }

    [BuiltCliFact]
    public async Task Turning_a_switch_on_is_written_to_the_manifest()
    {
        using var loadout = new LoadoutProcess();

        var project = await Registered(loadout);

        var run = await loadout.RunAsync(
            "project", "context", "tasks", "on", "--project", project);

        run.ExitCode.Should().Be(0, run.StandardError);

        // The manifest rather than the command's own report, because the
        // manifest is what the context compiler reads. A command that printed
        // the right sentence and wrote nothing is exactly the failure this
        // whole change exists to fix.
        var manifest = await File.ReadAllTextAsync(Manifest(loadout.Home, project));

        manifest.Should().Contain("tasks: true");
    }

    [BuiltCliFact]
    public async Task Both_switches_can_be_reached()
    {
        using var loadout = new LoadoutProcess();

        var project = await Registered(loadout);

        // The second switch had the identical defect and would have been
        // identically easy to leave out of the table.
        var run = await loadout.RunAsync(
            "project", "context", "code-map", "on", "--project", project);

        run.ExitCode.Should().Be(0, run.StandardError);

        var manifest = await File.ReadAllTextAsync(Manifest(loadout.Home, project));

        manifest.Should().Contain("code_map: true");
    }

    [BuiltCliFact]
    public async Task A_dry_run_changes_nothing()
    {
        using var loadout = new LoadoutProcess();

        var project = await Registered(loadout);

        var path = Manifest(loadout.Home, project);
        var before = await File.ReadAllBytesAsync(path);

        var run = await loadout.RunAsync(
            "project", "context", "tasks", "on", "--project", project, "--dry-run");

        run.ExitCode.Should().Be(0, "a preview of a write is not a failure");

        run.StandardOutput.Should().Contain("Nothing was written");

        // Byte for byte, not merely "the file is still there". The manifest
        // exists before and after either way, so a file listing would pass
        // whatever the command wrote into it.
        (await File.ReadAllBytesAsync(path)).Should().Equal(before,
            "--dry-run means the command changes nothing at all");
    }

    [BuiltCliFact]
    public async Task An_unknown_switch_is_refused_and_names_the_ones_that_exist()
    {
        using var loadout = new LoadoutProcess();

        var project = await Registered(loadout);

        var run = await loadout.RunAsync(
            "project", "context", "memory", "on", "--project", project);

        var said = run.StandardOutput + run.StandardError;

        run.ExitCode.Should().NotBe(0);

        // Naming them is the point: the switches live in a manifest most people
        // never open, so "no such switch" on its own leaves somebody guessing
        // at the spelling of one they cannot see.
        said.Should().Contain("tasks");
        said.Should().Contain("code-map");
    }

    [BuiltCliFact]
    public async Task Recording_a_task_says_when_nothing_will_show_it()
    {
        using var loadout = new LoadoutProcess();

        var project = await Registered(loadout);

        var quiet = await loadout.RunAsync(
            "task", "declare", "first-thing", "doing", "--project", project);

        quiet.ExitCode.Should().Be(0, quiet.StandardError);

        // The whole failure this change is about: a session writes to the
        // record, the write succeeds, and no session is ever shown it.
        quiet.StandardOutput.Should().Contain("does not carry its tasks");

        await loadout.RunAsync("project", "context", "tasks", "on", "--project", project);

        var loud = await loadout.RunAsync(
            "task", "declare", "second-thing", "doing", "--project", project);

        loud.ExitCode.Should().Be(0, loud.StandardError);

        // And it stops saying so once it is not true. A warning that never goes
        // away is one people learn to read past.
        loud.StandardOutput.Should().NotContain("does not carry its tasks");
    }

    [BuiltCliFact]
    public async Task Project_show_reports_what_is_carried()
    {
        using var loadout = new LoadoutProcess();

        var project = await Registered(loadout);

        await loadout.RunAsync("project", "context", "tasks", "on", "--project", project);

        var run = await loadout.RunAsync("project", "show", project, "--json");

        run.ExitCode.Should().Be(0, run.StandardError);

        // Where somebody looks first when asking what a project is set up to
        // do. The switches were invisible here as well as unsettable, so a
        // project carrying its tasks looked identical to one that was not.
        var context = run.Json().GetProperty("context");

        context.GetProperty("tasks").GetBoolean().Should().BeTrue();
        context.GetProperty("code-map").GetBoolean().Should().BeFalse();
    }

    /// <summary>The project manifest inside the throwaway home's workspace.</summary>
    private static string Manifest(string home, string slug) =>
        Directory
            .EnumerateFiles(home, "project.yaml", SearchOption.AllDirectories)
            .Single(path =>
                Path.GetFileName(Path.GetDirectoryName(path)!)
                    .Equals(slug, StringComparison.OrdinalIgnoreCase));

    /// <summary>Registers a repository, so the project resolves as a versioned one.</summary>
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
