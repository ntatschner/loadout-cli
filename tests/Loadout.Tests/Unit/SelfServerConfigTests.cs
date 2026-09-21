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

    /// <summary>
    /// The development host, driven rather than waited for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the branch that was wrong, and a test cannot reach it by asking
    /// what is running: under <c>dotnet test</c> the process is
    /// <c>testhost.exe</c>, so anything calling the real resolver takes the
    /// shipped-launcher branch and passes without touching the host case at
    /// all. The first test written for this fix did exactly that, and a probe
    /// printing what it had resolved was what caught it.
    /// </para>
    /// <para>
    /// So the decision is told what is running. What it must produce is a
    /// command that can be executed with those arguments: the host, then the
    /// assembly the host is to run, then the launcher's own words. The shape
    /// that shipped was the host followed straight by <c>mcp</c>, which dotnet
    /// has no command for.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// The host is named and the path around it is built with this platform's
    /// own separator. The first version hard-coded a Windows path, passed here
    /// and failed on Ubuntu and macOS: a backslash is an ordinary character in
    /// a Unix path, so GetFileNameWithoutExtension finds no separator, returns
    /// the whole string, and it is not "dotnet".
    ///
    /// The production code was right either way - it only ever sees a path its
    /// own operating system gave it - so the test was the thing that depended on
    /// which machine ran it. Built this way the host case runs everywhere rather
    /// than being skipped on two platforms, which is worth more than marking it
    /// Windows-only.
    /// </remarks>
    [Theory]
    [InlineData("dotnet")]
    [InlineData("dotnet.exe")]
    [InlineData("DOTNET.EXE")]
    public void Under_the_dotnet_host_the_assembly_comes_before_the_launchers_own_arguments(
        string hostName)
    {
        var host = Path.Combine(Path.GetTempPath(), "dotnet-home", hostName);
        var appDirectory = Path.Combine(Path.GetTempPath(), "loadout-app");

        var parts = Loadout.Core.Agents.LauncherInvocation.From(
            host, "loadout", appDirectory, _ => true);

        parts.Should().NotBeNull("the host case is recoverable, not a reason to give up");

        var (command, prefix) = parts!.Value;

        command.Should().Be(host);

        prefix.Should().ContainSingle()
            .Which.Should().Be(Path.Combine(appDirectory, "loadout.dll"),
                "dotnet has no 'mcp' command, so it has to be given something to run");
    }

    [Fact]
    public void A_shipped_launcher_is_its_own_command_with_nothing_in_front()
    {
        var launcher = Path.Combine(Path.GetTempPath(), "loadout-app", "loadout.exe");

        var parts = Loadout.Core.Agents.LauncherInvocation.From(
            launcher, "loadout", Path.GetTempPath(), _ => true);

        parts!.Value.Command.Should().Be(launcher);
        parts.Value.Prefix.Should().BeEmpty();
    }

    [Fact]
    public void The_host_case_declines_when_there_is_no_assembly_to_run()
    {
        // Declining is right here. A server entry naming a path that is not
        // there fails the agent's own startup rather than the launcher's, which
        // is a confusing place to find out.
        var host = Path.Combine(Path.GetTempPath(), "dotnet-home", "dotnet");

        Loadout.Core.Agents.LauncherInvocation.From(host, "loadout", Path.GetTempPath(), _ => false)
            .Should().BeNull();

        Loadout.Core.Agents.LauncherInvocation.From(host, null, Path.GetTempPath(), _ => true)
            .Should().BeNull();
    }

    [Fact]
    public void Nothing_running_is_nothing_declared() =>
        Loadout.Core.Agents.LauncherInvocation.From(null, "loadout", Path.GetTempPath(), _ => true)
            .Should().BeNull();

    /// <summary>
    /// A development build declares something that can actually be run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The shipped launcher is one executable and its process path is the whole
    /// answer. A development build is <c>dotnet loadout.dll</c>, whose process
    /// path is <c>dotnet.exe</c> — and <c>dotnet.exe</c> exists, so the guard
    /// that declines when the launcher cannot find itself passed happily and
    /// wrote a server whose command was the host and whose first argument was
    /// <c>mcp</c>. There is no such dotnet command.
    /// </para>
    /// <para>
    /// The agent started; the server did not. Every team run from a development
    /// build lost the tool its lead answers permission questions with, and the
    /// lead died within seconds reporting
    /// <c>mcp__loadout__loadout_permission not found</c> — which reads as a
    /// broken permission harness rather than as a path, and was diagnosed as
    /// three other things first.
    /// </para>
    /// <para>
    /// Asserted through <see cref="Loadout.Core.Agents.LauncherInvocation"/>
    /// rather than by running under the host, because a test cannot choose what
    /// is running it. What is checked is that both halves of that type agree:
    /// whatever the shell form puts in its quoted string, the split form puts
    /// in command and arguments, so a caller declaring a process and a caller
    /// writing a shell line cannot describe different launchers.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_shell_form_and_the_split_form_describe_the_same_launcher()
    {
        var parts = Loadout.Core.Agents.LauncherInvocation.Parts();
        var line = Loadout.Core.Agents.LauncherInvocation.Current();

        if (parts is null)
        {
            line.Should().BeNull("neither form may claim to know what the other could not work out");

            return;
        }

        var (command, prefix) = parts.Value;

        File.Exists(command).Should().BeTrue(
            "a server entry pointing at a path that is not there fails the agent's own startup");

        // Every part of the split form, quoted, is the shell form. Built the
        // same way round as the type builds it, so the assertion is that the
        // parts are the same parts rather than that the string was formatted.
        var rebuilt = string.Join(' ', new[] { command }.Concat(prefix).Select(one => "\"" + one + "\""));

        line.Should().Be(rebuilt);

        // And the part that was wrong: under the host there is a prefix, and it
        // is the assembly the host has to be given.
        if (Path.GetFileNameWithoutExtension(command).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            prefix.Should().ContainSingle()
                .Which.Should().EndWith(".dll", "the host needs something to run");
        }
        else
        {
            prefix.Should().BeEmpty("a shipped launcher is its own command");
        }
    }

    [Fact]
    public void The_declared_arguments_start_with_whatever_has_to_come_first()
    {
        var warnings = new List<string>();

        // No executablePath, so it works out how it is being started - which is
        // the path that was broken, and the one no test took.
        var files = SelfServerConfig.Write(true, "loadout-cli", _runtime, warnings);

        if (files.Count == 0)
        {
            warnings.Should().ContainSingle()
                .Which.Should().Contain("could not work out its own path");

            return;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(files[0]));

        var server = document.RootElement.GetProperty("mcpServers").GetProperty("loadout");
        var command = server.GetProperty("command").GetString()!;
        var args = server.GetProperty("args").EnumerateArray().Select(a => a.GetString()).ToList();

        // Whatever is running this, the declaration has to be runnable: the
        // first argument is 'mcp' for a shipped launcher, and the assembly
        // before it under the host.
        if (Path.GetFileNameWithoutExtension(command).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            args[0].Should().EndWith(".dll", "dotnet has no 'mcp' command to run");
            args[1].Should().Be("mcp");
            args[2].Should().Be("serve");
        }
        else
        {
            args[0].Should().Be("mcp");
            args[1].Should().Be("serve");
        }

        args.Should().Contain("--project").And.Contain("loadout-cli");
    }
}
