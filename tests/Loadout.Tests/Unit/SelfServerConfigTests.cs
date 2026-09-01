using System.Text.Json;
using FluentAssertions;
using Loadout.Agents;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Offering the launcher's own tools to the agent it starts.
/// </summary>
/// <remarks>
/// Two things matter here and neither is the JSON. It must not reach the
/// workspace, because it names one machine's executable and the workspace is
/// shared; and it must decline quietly when it cannot be honest, because a
/// server entry pointing at a path that is not there fails the agent's startup
/// rather than the launcher's.
/// </remarks>
public sealed class SelfServerConfigTests : IDisposable
{
    private readonly string _runtime;
    private readonly string _executable;

    public SelfServerConfigTests()
    {
        _runtime = Path.Combine(Path.GetTempPath(), "loadout-self-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_runtime);

        _executable = Path.Combine(_runtime, "loadout.exe");

        File.WriteAllText(_executable, "not really a launcher");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_runtime))
            {
                Directory.Delete(_runtime, recursive: true);
            }
        }
        catch (IOException)
        {
            // A temp directory that outlives the run is not a failed test.
        }
    }

    [Fact]
    public void It_declares_the_launcher_against_this_machine_and_this_project()
    {
        var warnings = new List<string>();

        var files = SelfServerConfig.Write(
            enabled: true, "starstats", _runtime, warnings, _executable);

        files.Should().ContainSingle();
        warnings.Should().BeEmpty();

        using var document = JsonDocument.Parse(File.ReadAllText(files[0]));

        var server = document.RootElement.GetProperty("mcpServers").GetProperty("loadout");

        server.GetProperty("command").GetString().Should().Be(_executable);

        var args = server.GetProperty("args").EnumerateArray()
            .Select(a => a.GetString()).ToList();

        // The project has to be named: the tools answer about one, and working
        // it out from a working directory is what the launcher already got
        // wrong once for the editor.
        args.Should().Equal("mcp", "serve", "--project", "starstats");
    }

    [Fact]
    public void It_is_written_where_the_launch_can_take_it_away_again()
    {
        var warnings = new List<string>();

        var files = SelfServerConfig.Write(
            enabled: true, "starstats", _runtime, warnings, _executable);

        // Into the runtime directory, never the workspace. It names one
        // machine's executable, and the workspace is cloned onto others where
        // that path is wrong — which is the fault the MCP service exists to
        // warn about.
        Path.GetDirectoryName(files[0]).Should().Be(_runtime);
        Path.GetFileName(files[0]).Should().Be(SelfServerConfig.FileName);
    }

    [Fact]
    public void Turning_it_off_declares_nothing_and_says_nothing()
    {
        var warnings = new List<string>();

        SelfServerConfig.Write(enabled: false, "starstats", _runtime, warnings, _executable)
            .Should().BeEmpty();

        // Somebody who turned it off does not need telling on every launch.
        warnings.Should().BeEmpty();
        File.Exists(Path.Combine(_runtime, SelfServerConfig.FileName)).Should().BeFalse();
    }

    [Fact]
    public void An_executable_that_is_not_there_is_declined_with_a_reason()
    {
        var warnings = new List<string>();

        var files = SelfServerConfig.Write(
            enabled: true,
            "starstats",
            _runtime,
            warnings,
            Path.Combine(_runtime, "gone.exe"));

        // Declaring it anyway would fail the agent's own startup, which is a
        // confusing place to find out the launcher could not locate itself.
        files.Should().BeEmpty();
        warnings.Should().ContainSingle().Which.Should().Contain("its own path");
    }
}
