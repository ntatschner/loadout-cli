using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// A manual run with nobody at a terminal: refused where nothing could hold
/// its questions, and let through where a daemon serves the dashboard, which
/// holds every one of them for a person to answer on the page.
/// </summary>
/// <remarks>
/// Studio's Start a run sheet starts a run through the daemon, with nobody at
/// a terminal, and the design answers a manual run's briefs on the page. The
/// refusal used to stand whether a daemon was there or not.
/// </remarks>
[Collection(ContractCollection.Name)]
public sealed class ManualRunContractTests
{
    [BuiltCliFact]
    public async Task With_nothing_to_hold_its_questions_a_manual_run_is_refused()
    {
        using var loadout = new LoadoutProcess();
        var project = await ProjectAsync(loadout);

        var run = await loadout.RunAsync("team", "run", "iterating-project", "Make the docs true.", "--project", project, "--autonomy", "manual", "--non-interactive");

        run.ExitCode.Should().NotBe(0);
        (run.StandardOutput + run.StandardError).Should().Contain("Manual mode asks a person at every step");
    }

    [BuiltCliFact]
    public async Task With_a_daemon_serving_the_dashboard_a_manual_run_holds_its_first_question_for_the_page()
    {
        using var loadout = new LoadoutProcess();
        var project = await ProjectAsync(loadout);
        var state = await StateAsync(loadout);
        await DaemonNoteAsync(state);

        // No agent on the path: a manual run asks before it starts anything,
        // so nothing is started, and the question is what this waits for.
        loadout.Environment["PATH"] = GitDirectory();

        using var run = loadout.Start("team", "run", "iterating-project", "Make the docs true.", "--project", project, "--autonomy", "manual", "--non-interactive");

        try
        {
            var until = DateTime.UtcNow.AddSeconds(60);
            string[] asked = [];

            while (DateTime.UtcNow < until && !run.HasExited)
            {
                asked = Directory.EnumerateFiles(state, "ask-*.json", SearchOption.AllDirectories).ToArray();

                if (asked.Length > 0)
                {
                    break;
                }

                await Task.Delay(250);
            }

            run.HasExited.Should().BeFalse("it waits for a person rather than being refused for want of a terminal");
            asked.Should().NotBeEmpty("its first question is held for the page to answer");
        }
        finally
        {
            if (!run.HasExited)
            {
                run.Kill(entireProcessTree: true);
            }
        }
    }

    private static async Task<string> ProjectAsync(LoadoutProcess loadout)
    {
        var directory = Path.Combine(loadout.Home, "repo");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "README.md"), "# trial\n");
        await Git(directory, "init", "-q");
        await Git(directory, "add", "-A");
        await Git(directory, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-qm", "init");

        var added = await loadout.RunAsync("project", "add", directory, "--non-interactive");
        added.ExitCode.Should().Be(0, added.StandardOutput + added.StandardError);

        return "repo";
    }

    private static async Task<string> StateAsync(LoadoutProcess loadout)
    {
        var doctor = await loadout.RunAsync("doctor", "--json");

        return doctor.Json().GetProperty("checks").EnumerateArray()
            .First(check => check.GetProperty("name").GetString() == "State").GetProperty("detail").GetString()!;
    }

    /// <summary>The note a daemon leaves, naming this test run, which is alive.</summary>
    private static async Task DaemonNoteAsync(string state)
    {
        using var self = System.Diagnostics.Process.GetCurrentProcess();
        var teams = Path.Combine(state, "teams");

        Directory.CreateDirectory(teams);
        await File.WriteAllTextAsync(
            Path.Combine(teams, "daemon.json"),
            JsonSerializer.Serialize(new { Pid = Environment.ProcessId, StartedAt = self.StartTime.ToUniversalTime(), Address = "http://127.0.0.1:65124/", Since = DateTimeOffset.UtcNow }));
    }

    private static string GitDirectory()
    {
        var name = OperatingSystem.IsWindows() ? "git.exe" : "git";

        return (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .First(directory => File.Exists(Path.Combine(directory, name)));
    }

    private static async Task Git(string directory, params string[] arguments)
    {
        var start = new System.Diagnostics.ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = System.Diagnostics.Process.Start(start)!;
        await process.WaitForExitAsync();
        process.ExitCode.Should().Be(0, await process.StandardError.ReadToEndAsync());
    }
}
