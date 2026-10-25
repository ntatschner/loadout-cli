using FluentAssertions;
using Loadout.Cli;
using Loadout.Cli.Infrastructure;
using Loadout.Platform.Abstractions;
using Loadout.Platform.Common;
using Loadout.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Running a command inside this process: each call with its own output,
/// directory and console, and a count of anything that starts the launcher
/// again instead.
/// </summary>
public sealed class CommandHostTests
{
    private static CommandHost Host(IProcessLauncher? processes = null)
    {
        var services = new ServiceCollection();

        services.AddSingleton<IEnvironmentProvider>(new FakeEnvironmentProvider(Path.GetTempPath()));
        services.AddSingleton(processes ?? new StubProcessLauncher(string.Empty));

        var host = new CommandHost(Program.Accessible);

        host.Freeze(services);

        return host;
    }

    private static async Task<(int Code, string Output, string Error)> RunAsync(CommandHost host, params string[] words)
    {
        await using var run = host.Start(words, Path.GetTempPath());

        var output = run.Output.ReadToEndAsync(CancellationToken.None);
        var error = run.Error.ReadToEndAsync(CancellationToken.None);

        return (await run.Exited, await output, await error);
    }

    [Fact]
    public async Task Commands_run_at_once_each_hear_only_what_they_said()
    {
        var host = Host();

        // One writes its document straight to standard output, the way every
        // --json failure does; the other writes through its console. Neither
        // needs a service, so nothing here reaches the machine.
        var runs = Enumerable.Range(0, 16)
            .Select(i => i % 2 == 0
                ? RunAsync(host, "memory", "show", "--json")
                : RunAsync(host, "--version"))
            .ToList();

        var done = await Task.WhenAll(runs);

        for (var i = 0; i < done.Length; i++)
        {
            if (i % 2 == 0)
            {
                done[i].Code.Should().NotBe(0);
                done[i].Output.Should().Contain("\"error\"").And.NotContain(Version());
            }
            else
            {
                done[i].Code.Should().Be(0);
                done[i].Output.Trim().Should().Be(Version());
            }
        }
    }

    [Fact]
    public async Task Output_written_outside_any_call_goes_where_it_always_went()
    {
        var host = Host();

        // Routing is installed by the first call.
        await RunAsync(host, "--version");

        var before = Console.Out;
        using var captured = new StringWriter();

        Console.SetOut(captured);

        try
        {
            // Replaced since routing was installed, as a test capturing the
            // console does; the next call routes again, round this writer.
            var inside = await RunAsync(host, "--version");

            Console.Out.WriteLine("the daemon's own line");

            inside.Output.Should().NotContain("the daemon's own line");
            captured.ToString().Should().Contain("the daemon's own line").And.NotContain(Version());
        }
        finally
        {
            Console.SetOut(before);
        }
    }

    [Fact]
    public void An_invocation_answers_for_its_own_directory_and_streams()
    {
        var directory = Path.Combine(Path.GetTempPath(), "somewhere-else");
        var invocation = new Invocation(TextWriter.Null, TextWriter.Null, TextReader.Null, directory);

        using (invocation.Enter())
        {
            Invocation.CurrentDirectory.Should().Be(directory);
            Invocation.IsOutputRedirected.Should().BeTrue("a captured call's output is a pipe, as a child's was");
            Invocation.IsInputRedirected.Should().BeTrue();
        }

        Invocation.Current.Should().BeNull();
        Invocation.CurrentDirectory.Should().Be(Directory.GetCurrentDirectory());
    }

    [Fact]
    public async Task Starting_the_launcher_again_and_waiting_on_it_is_counted()
    {
        var stub = new StubProcessLauncher(string.Empty);
        var host = Host(stub);

        using var provider = ServicesOf(host);

        var processes = provider.GetRequiredService<IProcessLauncher>();
        var launcher = Loadout.Core.Agents.LauncherInvocation.Parts();

        launcher.Should().NotBeNull("the test host can name how it was started");

        var (command, prefix) = launcher!.Value;

        await processes.RunAsync(new ProcessRequest(command, [.. prefix, "project", "list"]));
        await processes.RunAsync(new ProcessRequest("git", ["status"]));
        processes.StartBackground(new ProcessRequest(command, [.. prefix, "team", "run"]));

        host.LauncherChildren.Should().Be(1,
            "the launcher started and waited on counts; git does not, and nor does a driver meant to outlive its caller");

        stub.Requests.Should().HaveCount(3, "counting passes every start on");
    }

    [Theory]
    [InlineData(@"C:\tools\loadout.exe", new string[0], @"C:\tools\loadout.exe", new[] { "project" }, true)]
    [InlineData(@"C:\dotnet\dotnet.exe", new[] { @"C:\src\loadout.dll" }, @"C:\dotnet\dotnet.exe", new[] { @"C:\src\loadout.dll", "list" }, true)]
    [InlineData(@"C:\dotnet\dotnet.exe", new[] { @"C:\src\loadout.dll" }, @"C:\dotnet\dotnet.exe", new[] { @"C:\other\tool.dll" }, false)]
    [InlineData(@"C:\tools\loadout.exe", new string[0], "loadout", new[] { "project" }, false)]
    [InlineData(@"C:\tools\loadout.exe", new string[0], @"C:\tools\git.exe", new[] { "status" }, false)]
    public void Whether_a_request_starts_the_launcher(
        string launcher, string[] prefix, string executable, string[] arguments, bool expected)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        CommandHost.Starts(new ProcessRequest(executable, arguments), (launcher, prefix)).Should().Be(expected);
    }

    [Fact]
    public async Task A_pipe_reads_back_lines_as_they_were_written()
    {
        var pipe = new TextPipe();

        var first = pipe.Reader.ReadLineAsync(CancellationToken.None).AsTask();

        first.IsCompleted.Should().BeFalse("nothing has been written");

        pipe.Writer.Write("one\r\ntw");
        (await first).Should().Be("one");

        pipe.Writer.Write("o\nthree");
        pipe.Close();

        (await pipe.Reader.ReadLineAsync(CancellationToken.None)).Should().Be("two");
        (await pipe.Reader.ReadLineAsync(CancellationToken.None)).Should().Be("three");
        (await pipe.Reader.ReadLineAsync(CancellationToken.None)).Should().BeNull();

        pipe.Writer.Write("after the end\n");
        (await pipe.Reader.ReadLineAsync(CancellationToken.None)).Should().BeNull("a closed pipe takes nothing more");
    }

    private static string Version() =>
        typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>The services a call gets, built as the host builds them.</summary>
    private static ServiceProvider ServicesOf(CommandHost host)
    {
        var services = new ServiceCollection();

        foreach (var descriptor in host.Baseline)
        {
            ((ICollection<ServiceDescriptor>)services).Add(descriptor);
        }

        return services.BuildServiceProvider();
    }
}
