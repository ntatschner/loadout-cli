using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// What 'statusline install' and 'statusline show' say about the refresh
/// interval, through the real binary and against a throwaway Claude home.
/// <para>
/// The version is read from a stand-in <c>claude</c> on a path holding nothing
/// else, so the outcome does not depend on which Claude Code this machine has,
/// or whether it has one at all.
/// </para>
/// </summary>
[Collection(ContractCollection.Name)]
public sealed class StatuslineRefreshContractTests
{
    private static LoadoutProcess Isolated(string? claudeVersion)
    {
        var loadout = new LoadoutProcess();
        var claudeHome = Path.Combine(loadout.Home, ".claude");
        var bin = Path.Combine(loadout.Home, "bin");

        Directory.CreateDirectory(claudeHome);
        Directory.CreateDirectory(bin);

        if (claudeVersion is not null)
        {
            if (OperatingSystem.IsWindows())
            {
                File.WriteAllText(Path.Combine(bin, "claude.cmd"), $"@echo {claudeVersion}\r\n");
            }
            else
            {
                var script = Path.Combine(bin, "claude");

                File.WriteAllText(script, $"#!/bin/sh\necho '{claudeVersion}'\n");
                File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        loadout.Environment["CLAUDE_CONFIG_DIR"] = claudeHome;

        // The system directory stays so a batch file can still find its shell.
        loadout.Environment["PATH"] = OperatingSystem.IsWindows()
            ? bin + Path.PathSeparator + Environment.SystemDirectory
            : bin + Path.PathSeparator + "/bin" + Path.PathSeparator + "/usr/bin";

        return loadout;
    }

    private static string SettingsPath(LoadoutProcess loadout) =>
        Path.Combine(loadout.Home, ".claude", "settings.json");

    [BuiltCliFact]
    public async Task Install_dry_run_changes_nothing_and_names_the_interval()
    {
        using var loadout = Isolated("2.1.308 (Claude Code)");

        const string before = """{ "theme": "dark" }""";
        File.WriteAllText(SettingsPath(loadout), before);

        var run = await loadout.RunAsync("statusline", "install", "--global", "--dry-run");

        run.ExitCode.Should().Be(0, run.StandardError);
        run.StandardOutput.Should().Contain("refreshInterval 30");
        run.StandardOutput.Should().Contain("Nothing was changed.");
        File.ReadAllText(SettingsPath(loadout)).Should().Be(before, "a dry run writes nothing");
    }

    [BuiltCliFact]
    public async Task Install_writes_the_interval_for_a_Claude_Code_that_reads_it()
    {
        using var loadout = Isolated("2.1.308 (Claude Code)");

        var run = await loadout.RunAsync("statusline", "install", "--global");

        run.ExitCode.Should().Be(0, run.StandardError);
        run.StandardOutput.Should().Contain("ticks every 30s");

        using var written = JsonDocument.Parse(File.ReadAllText(SettingsPath(loadout)));

        written.RootElement.GetProperty("statusLine").GetProperty("refreshInterval").GetInt32()
            .Should().Be(30);
    }

    [BuiltCliFact]
    public async Task Install_for_an_older_Claude_Code_says_it_updates_on_redraw_only()
    {
        using var loadout = Isolated("2.1.99 (Claude Code)");

        var run = await loadout.RunAsync("statusline", "install", "--global");

        run.ExitCode.Should().Be(0, run.StandardError);
        run.StandardOutput.Should().Contain("updates on redraw only");
        File.ReadAllText(SettingsPath(loadout)).Should().NotContain("refreshInterval");
    }

    [BuiltCliFact]
    public async Task Install_under_redraw_never_says_there_is_no_ticking()
    {
        using var loadout = Isolated("2.1.308 (Claude Code)");

        (await loadout.RunAsync("config", "set", "show-redraw", "never")).ExitCode.Should().Be(0);

        var run = await loadout.RunAsync("statusline", "install", "--global");

        run.ExitCode.Should().Be(0, run.StandardError);
        run.StandardOutput.Should().Contain("no ticking");
        File.ReadAllText(SettingsPath(loadout)).Should().NotContain("refreshInterval");
    }

    [BuiltCliFact]
    public async Task Show_tells_you_to_reinstall_when_the_interval_is_missing()
    {
        using var loadout = Isolated(claudeVersion: null);

        File.WriteAllText(SettingsPath(loadout), """
            { "statusLine": { "type": "command", "command": "\"C:\\loadout.exe\" statusline" } }
            """);

        var run = await loadout.RunAsync("statusline", "show", "--global");

        run.ExitCode.Should().Be(0, run.StandardError);
        run.StandardOutput.Should().Contain("Reinstall with loadout statusline install");

        var json = await loadout.RunAsync("statusline", "show", "--global", "--json");

        using var shown = JsonDocument.Parse(json.StandardOutput);
        var installed = shown.RootElement.GetProperty("installed")[0];

        installed.TryGetProperty("refresh", out var refresh).Should().BeFalse(
            "a null is left out of the JSON, and there is no interval to report, got {0}", refresh);
    }

    [BuiltCliFact]
    public async Task Show_says_nothing_about_reinstalling_when_the_interval_is_there()
    {
        using var loadout = Isolated(claudeVersion: null);

        File.WriteAllText(SettingsPath(loadout), """
            { "statusLine": { "type": "command", "command": "\"C:\\loadout.exe\" statusline", "refreshInterval": 30 } }
            """);

        var run = await loadout.RunAsync("statusline", "show", "--global");

        run.ExitCode.Should().Be(0, run.StandardError);
        run.StandardOutput.Should().NotContain("Reinstall with");

        var json = await loadout.RunAsync("statusline", "show", "--global", "--json");

        using var shown = JsonDocument.Parse(json.StandardOutput);

        shown.RootElement.GetProperty("installed")[0].GetProperty("refresh").GetInt32().Should().Be(30);
    }
}
