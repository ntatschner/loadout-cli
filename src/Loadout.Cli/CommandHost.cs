using Loadout.Cli.Infrastructure;
using Loadout.Core.Agents;
using Loadout.Core.Teams.Daemon;
using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;
using Loadout.Platform.Common;
using Loadout.Tui;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli;

/// <summary>
/// Runs a command inside this process, through a parser configured exactly
/// like the command line's, each call with services of its own.
/// </summary>
/// <remarks>
/// <para>
/// The daemon used to start this launcher again for every page Studio read
/// and every button pressed: a process starting, a container built, one
/// command run and the process gone. Most of a page's wait was that. This
/// runs the same command tree in the daemon instead, so what a page shows is
/// still exactly what the command says.
/// </para>
/// <para>
/// Each call is built from a copy of the registrations the launcher started
/// with, taken before any parser added to them, so a call gets fresh
/// services as a new process did and two calls at once cannot reach into
/// each other's. The parser used to be run against the one shared collection,
/// which every call added its commands to again: a race between calls, and a
/// collection that grew for as long as the daemon ran.
/// </para>
/// <para>
/// What a command asks of the process - its streams, whether they are
/// redirected, the directory it was started in - it asks through
/// <see cref="Invocation"/>, which a captured call answers for itself.
/// </para>
/// </remarks>
public sealed class CommandHost
{
    /// <summary>How wide a console writing into a pipe is.</summary>
    private const int Redirected = 80;

    private readonly AsyncLocal<IAnsiConsole?> _console = new();
    private readonly Func<string[], IServiceProvider, AccessibleMode> _accessible;
    private ServiceDescriptor[] _baseline = [];
    private long _launcherChildren;

    /// <summary>Creates one; <see cref="Freeze"/> gives it the registrations to copy.</summary>
    /// <param name="accessible">How a call's arguments settle its accessibility profile.</param>
    internal CommandHost(Func<string[], IServiceProvider, AccessibleMode> accessible)
    {
        ArgumentNullException.ThrowIfNull(accessible);

        _accessible = accessible;
    }

    /// <summary>
    /// How many times this process has started this launcher as a child it
    /// waits on.
    /// </summary>
    /// <remarks>
    /// In the daemon, zero is the point of all this, and the count is what
    /// proves it stays zero: it is kept on the one process launcher everything
    /// here starts processes through, so a command run by starting a launcher
    /// again is counted however it came to be started. A child started to
    /// outlive its caller - a team run's driver, which has to survive the
    /// daemon restarting - is not counted: that is a command's work, not a way
    /// of running one.
    /// </remarks>
    internal long LauncherChildren => Interlocked.Read(ref _launcherChildren);

    /// <summary>The registrations every call is built from.</summary>
    internal IReadOnlyList<ServiceDescriptor> Baseline => _baseline;

    /// <summary>
    /// Counts on the process launcher, and takes the registrations every call
    /// copies.
    /// </summary>
    /// <remarks>
    /// Called once the launcher has registered everything and before any
    /// parser has, because a parser adds each command it knows to whatever
    /// collection it is handed.
    /// </remarks>
    internal void Freeze(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.LastOrDefault(one => one.ServiceType == typeof(IProcessLauncher)) is
            { ImplementationInstance: IProcessLauncher processes } registered)
        {
            services.Remove(registered);
            services.AddSingleton<IProcessLauncher>(new Counting(processes, this));
        }

        _baseline = [.. services];
    }

    /// <summary>
    /// Runs one command, its output going wherever the caller's console and
    /// invocation say, or to this process's own when it has neither.
    /// </summary>
    /// <param name="arguments">Everything after <c>loadout</c>.</param>
    /// <param name="ct">Stops the command.</param>
    internal Task<int> RunAsync(string[] arguments, CancellationToken ct) =>
        RunAsync(arguments, _console.Value, ct);

    /// <summary>
    /// Starts a command in this process with its output captured, as a child
    /// with piped output and its input closed would have run.
    /// </summary>
    /// <param name="arguments">Everything after <c>loadout</c>.</param>
    /// <param name="directory">The directory it runs in.</param>
    /// <param name="terminal">
    /// The terminal it was typed in, when it was handed over by the command
    /// line, which it is drawn for instead of a pipe.
    /// </param>
    internal IPipedProcess Start(IReadOnlyList<string> arguments, string directory, ForwardedTerminal? terminal = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        Invocation.RouteStandardStreams();

        return new InProcessRun(this, [.. arguments], directory, terminal);
    }

    private async Task<int> RunAsync(string[] arguments, IAnsiConsole? console, CancellationToken ct)
    {
        var (launcherArgs, passthrough) = PassthroughArguments.Split(arguments);
        var services = new ServiceCollection();

        foreach (var descriptor in _baseline)
        {
            ((ICollection<ServiceDescriptor>)services).Add(descriptor);
        }

        services.AddSingleton(new PassthroughArguments(passthrough));
        services.AddSingleton(sp => _accessible(launcherArgs, sp));
        services.AddSingleton<ICommandCatalogue>(_ => new CommandCatalogue(RunAsync));

        if (console is not null)
        {
            services.AddSingleton(console);
        }

        var registrar = new TypeRegistrar(services);
        var json = launcherArgs.Contains("--json", StringComparer.Ordinal);
        var app = new CommandApp(registrar);

        app.Configure(config =>
        {
            if (console is not null)
            {
                config.Settings.Console = console;
            }

            Program.Configure(config, launcherArgs.Contains("--debug", StringComparer.Ordinal), json);
        });

        try
        {
            return await app.RunAsync(Program.Rewrite(launcherArgs), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return (int)ExitCode.Interrupted;
        }
    }

    /// <summary>
    /// What the command line does to its console before the first thing is
    /// drawn, done once to a captured call's, before its command runs.
    /// </summary>
    private void Settle(IAnsiConsole console, string[] arguments, bool forwarded)
    {
        var (launcherArgs, _) = PassthroughArguments.Split(arguments);
        var services = new ServiceCollection();

        foreach (var descriptor in _baseline)
        {
            ((ICollection<ServiceDescriptor>)services).Add(descriptor);
        }

        using var provider = services.BuildServiceProvider();
        var accessible = _accessible(launcherArgs, provider);

        // A terminal's own NO_COLOR is in the shape it sent, not the daemon's environment.
        accessible.Apply(console, forwarded ? null : provider.GetRequiredService<IEnvironmentProvider>().GetVariable("NO_COLOR"));

        if (accessible.IsOn
            && !launcherArgs.Contains("--json", StringComparer.Ordinal)
            && !launcherArgs.Contains("--quiet", StringComparer.Ordinal)
            && !launcherArgs.Contains("-q", StringComparer.Ordinal))
        {
            Console.Out.WriteLine(accessible.Line);
        }
    }

    /// <summary>One command running in this process, seen as the child it replaces.</summary>
    private sealed class InProcessRun : IPipedProcess
    {
        private readonly TextPipe _output = new();
        private readonly TextPipe _error = new();
        private readonly CancellationTokenSource _stop = new();

        internal InProcessRun(CommandHost host, string[] arguments, string directory, ForwardedTerminal? terminal)
        {
            StartedAt = DateTimeOffset.UtcNow;

            // Its own console, writing where its output goes, set up as the
            // command line sets up one whose output is a pipe - not
            // interactive, and as wide as a redirected console is - or as the
            // terminal that handed it over says it is.
            var console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Out = new AnsiConsoleOutput(_output.Writer),
                Interactive = InteractionSupport.No,
                Ansi = terminal is null ? AnsiSupport.Detect : terminal.Ansi ? AnsiSupport.Yes : AnsiSupport.No,
                ColorSystem = terminal is null ? ColorSystemSupport.Detect : Colours(terminal.Colours),
            });

            // Pinned, because left to detect it Spectre came out wider inside
            // the daemon than the 80 a child writing into a pipe always got,
            // and every table wrapped somewhere other than it did before.
            console.Profile.Width = terminal?.Width ?? Redirected;

            var invocation = new Invocation(_output.Writer, _error.Writer, TextReader.Null, directory, toTerminal: terminal is not null);

            Exited = Task.Run(async () =>
            {
                using var entered = invocation.Enter();

                host._console.Value = console;

                try
                {
                    host.Settle(console, arguments, forwarded: terminal is not null);

                    return await host.RunAsync(arguments, console, _stop.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // What a child that threw past every handler would have
                    // left: a line on its error stream and a failing code.
                    _error.Writer.WriteLine(Core.Security.SecretRedactor.Redact(ex.Message));

                    return (int)ExitCode.GeneralFailure;
                }
                finally
                {
                    _output.Close();
                    _error.Close();
                }
            });
        }

        public int ProcessId => Environment.ProcessId;

        private static ColorSystemSupport Colours(int bits) => bits switch
        {
            24 => ColorSystemSupport.TrueColor,
            8 => ColorSystemSupport.EightBit,
            4 => ColorSystemSupport.Standard,
            3 => ColorSystemSupport.Legacy,
            _ => ColorSystemSupport.NoColors,
        };

        public DateTimeOffset StartedAt { get; }

        public TextWriter Input => TextWriter.Null;

        public TextReader Output => _output.Reader;

        public TextReader Error => _error.Reader;

        public Task<int> Exited { get; }

        public Task CloseInputAsync() => Task.CompletedTask;

        public void Kill()
        {
            try
            {
                _stop.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public async ValueTask DisposeAsync()
        {
            Kill();

            try
            {
                await Exited.ConfigureAwait(false);
            }
            finally
            {
                _stop.Dispose();
            }
        }
    }

    /// <summary>The process launcher, counting each start of this launcher it waits on.</summary>
    private sealed class Counting(IProcessLauncher inner, CommandHost host) : IProcessLauncher
    {
        public Task<OperationResult<ProcessOutcome>> RunAsync(ProcessRequest request, TimeSpan? timeout = null, CancellationToken ct = default)
        {
            Count(request);

            return inner.RunAsync(request, timeout, ct);
        }

        public Task<OperationResult<int>> RunInteractiveAsync(ProcessRequest request, CancellationToken ct = default)
        {
            Count(request);

            return inner.RunInteractiveAsync(request, ct);
        }

        public Task<OperationResult<IPipedProcess>> StartPipedAsync(ProcessRequest request, CancellationToken ct = default)
        {
            Count(request);

            return inner.StartPipedAsync(request, ct);
        }

        public OperationResult StartDetached(ProcessRequest request) => inner.StartDetached(request);

        public OperationResult<BackgroundProcess> StartBackground(ProcessRequest request) => inner.StartBackground(request);

        private void Count(ProcessRequest request)
        {
            if (IsLauncher(request))
            {
                Interlocked.Increment(ref host._launcherChildren);
            }
        }

        private static bool IsLauncher(ProcessRequest request) =>
            LauncherInvocation.Parts() is { } launcher && Starts(request, launcher);
    }

    /// <summary>Whether a request starts the launcher named by its parts.</summary>
    /// <remarks>
    /// The executable and the arguments that come before the launcher's own:
    /// on a development build that is dotnet and the launcher's dll, and dotnet
    /// on its own starts plenty that is not this.
    /// </remarks>
    internal static bool Starts(ProcessRequest request, (string Command, IReadOnlyList<string> Prefix) launcher)
    {
        ArgumentNullException.ThrowIfNull(request);

        return launcher.Command.Length > 0
            && Path.IsPathFullyQualified(request.Executable)
            && string.Equals(Path.GetFullPath(request.Executable), Path.GetFullPath(launcher.Command), StringComparison.OrdinalIgnoreCase)
            && request.Arguments.Take(launcher.Prefix.Count).SequenceEqual(launcher.Prefix, StringComparer.OrdinalIgnoreCase);
    }
}
