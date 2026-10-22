using System.Text.Json.Nodes;
using FluentAssertions;
using Loadout.Core.Statusline;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The refresh interval the status line installer writes, and the decision
/// behind it: a line with relative times needs Claude to redraw it while the
/// session is idle, but only a Claude Code known to read the key gets one, and
/// a screen-reader profile never does.
/// </summary>
public sealed class StatuslineRefreshTests : IDisposable
{
    private const string Executable = @"C:\Programs\loadout\loadout.exe";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "loadout-refresh-" + Guid.NewGuid().ToString("N")[..12]);

    private string SettingsPath => Path.Combine(_directory, "settings.json");

    public StatuslineRefreshTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private JsonObject Entry() =>
        (JsonNode.Parse(File.ReadAllText(SettingsPath))!["statusLine"] as JsonObject)!;

    [Fact]
    public async Task Install_writes_the_refresh_interval_in_seconds()
    {
        File.WriteAllText(SettingsPath, """{ "theme": "dark", "permissions": { "allow": ["Bash(ls)"] } }""");

        var result = await StatuslineInstaller.InstallAsync(
            SettingsPath, Executable, StatuslineInstaller.RefreshSeconds);

        result.Succeeded.Should().BeTrue(result.Error);
        result.Value!.Refresh.Should().Be(30);

        var root = JsonNode.Parse(File.ReadAllText(SettingsPath))!;

        root["statusLine"]!["refreshInterval"]!.GetValue<int>().Should().Be(30);
        root["statusLine"]!["type"]!.GetValue<string>().Should().Be("command");
        root["theme"]!.GetValue<string>().Should().Be("dark", "every other key survives");
        root["permissions"]!["allow"]![0]!.GetValue<string>().Should().Be("Bash(ls)");

        (await StatuslineInstaller.ReadRefreshAsync(SettingsPath)).Should().Be(30);
    }

    [Fact]
    public async Task Install_without_an_interval_leaves_it_out()
    {
        var result = await StatuslineInstaller.InstallAsync(SettingsPath, Executable);

        result.Succeeded.Should().BeTrue(result.Error);
        Entry().ContainsKey("refreshInterval").Should().BeFalse();
        (await StatuslineInstaller.ReadRefreshAsync(SettingsPath)).Should().BeNull();
    }

    [Fact]
    public async Task Reinstalling_without_an_interval_removes_an_earlier_one()
    {
        await StatuslineInstaller.InstallAsync(SettingsPath, Executable, StatuslineInstaller.RefreshSeconds);

        // What switching to the screen-reader profile and reinstalling does:
        // the ticking has to stop, not survive from the earlier install.
        await StatuslineInstaller.InstallAsync(SettingsPath, Executable, refreshSeconds: null);

        Entry().ContainsKey("refreshInterval").Should().BeFalse();
        Entry()["command"]!.GetValue<string>().Should().Be(StatuslineInstaller.CommandFor(Executable));
    }

    [Fact]
    public async Task Reading_the_interval_of_a_missing_or_broken_file_gives_none()
    {
        (await StatuslineInstaller.ReadRefreshAsync(SettingsPath)).Should().BeNull();

        File.WriteAllText(SettingsPath, "{ not json");

        (await StatuslineInstaller.ReadRefreshAsync(SettingsPath)).Should().BeNull();
    }

    [Theory]
    [InlineData("2.1.308 (Claude Code)", true)]
    [InlineData("2.1.309 (Claude Code)", true)]
    [InlineData("2.2.0", true)]
    [InlineData("10.0.0", true)]
    [InlineData("2.1.99 (Claude Code)", false)]
    [InlineData("2.1.307 (Claude Code)", false)]
    [InlineData("1.9.999", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("garbage", false)]
    public void SupportsRefresh_compares_the_version_as_numbers(string? versionLine, bool expected) =>
        StatuslineInstaller.SupportsRefresh(versionLine).Should().Be(expected);

    [Theory]
    [InlineData("never")]
    [InlineData("Never")]
    public void Redraw_never_installs_no_interval(string redraw) =>
        StatuslineInstaller.RefreshFor("2.1.308 (Claude Code)", redraw).Should().BeNull(
            "a line rewriting itself every half minute is noise to a screen reader");

    [Theory]
    [InlineData("2.1.99 (Claude Code)")]
    [InlineData(null)]
    public void A_Claude_Code_not_known_to_read_the_key_gets_no_interval(string? versionLine) =>
        StatuslineInstaller.RefreshFor(versionLine, "allowed").Should().BeNull();

    [Theory]
    [InlineData("allowed")]
    [InlineData(null)]
    public void A_supported_Claude_Code_that_may_redraw_ticks_every_thirty_seconds(string? redraw) =>
        StatuslineInstaller.RefreshFor("2.1.308 (Claude Code)", redraw).Should().Be(30);
}
