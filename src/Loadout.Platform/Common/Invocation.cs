using System.Text;

namespace Loadout.Platform.Common;

/// <summary>
/// What one command reads from the process it runs in, carried with the call
/// rather than set for the whole process.
/// </summary>
/// <remarks>
/// <para>
/// A command was written for a process that runs it once and exits, so it
/// asks the process for its standard streams, whether they are redirected
/// and which directory it was started in. Inside the daemon several commands
/// run at once in one process, and each needs its own answers: Studio's reads
/// are a pipe with nobody at it, started in the home directory, whatever the
/// daemon itself was started from.
/// </para>
/// <para>
/// Outside an invocation every property answers as the process does, so the
/// command line behaves exactly as it did. Nothing here is set process-wide:
/// it flows with the call through <see cref="AsyncLocal{T}"/>, so two
/// commands running at once cannot see each other's.
/// </para>
/// </remarks>
public sealed class Invocation
{
    private static readonly AsyncLocal<Invocation?> Ambient = new();

    /// <summary>Console.Out as it was left by routing, to tell whether something has replaced it since.</summary>
    private static TextWriter? _routed;

    /// <summary>Creates one.</summary>
    /// <param name="output">Where standard output goes.</param>
    /// <param name="error">Where standard error goes.</param>
    /// <param name="input">What standard input reads.</param>
    /// <param name="directory">The directory it runs in.</param>
    /// <param name="toTerminal">
    /// Whether its output is drawn for somebody's terminal rather than
    /// captured for a page, as a command typed in a terminal and handed to
    /// the daemon is. Its input is never a terminal: nobody can answer it.
    /// </param>
    public Invocation(TextWriter output, TextWriter error, TextReader input, string directory, bool toTerminal = false)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrEmpty(directory);

        Output = output;
        Error = error;
        Input = input;
        WorkingDirectory = Physical(directory);
        ToTerminal = toTerminal;
    }

    /// <summary>
    /// A directory as a process started in it would be told it is: on Unix,
    /// with every symbolic link along it resolved.
    /// </summary>
    /// <remarks>
    /// A Unix process asks the kernel where it is, and the kernel answers with
    /// the real path. On macOS the temporary directory is under <c>/var</c>,
    /// which is a link to <c>/private/var</c>, so a child started in
    /// <c>/var/folders/…</c> said <c>/private/var/folders/…</c>, and a command
    /// run in the daemon said the path it was handed, which is not the same
    /// answer. Windows answers with the path as it was set, links and all, so
    /// it is left alone there.
    /// <para>
    /// Asked of libc's realpath, which is the kernel's own answer, rather than
    /// worked out by following each link: an attempt that did that passed on
    /// Linux and still left <c>/var</c> unresolved on macOS.
    /// </para>
    /// </remarks>
    internal static string Physical(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            return directory;
        }

        try
        {
            return Unix.NativeTerminal.RealPath(Path.GetFullPath(directory)) ?? directory;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return directory;
        }
    }

    /// <summary>The invocation this call is part of, or null on the command line.</summary>
    public static Invocation? Current => Ambient.Value;

    /// <summary>Where this invocation's standard output goes.</summary>
    public TextWriter Output { get; }

    /// <summary>Where its standard error goes.</summary>
    public TextWriter Error { get; }

    /// <summary>What its standard input reads.</summary>
    public TextReader Input { get; }

    /// <summary>The directory it runs in.</summary>
    public string WorkingDirectory { get; }

    /// <summary>Whether its output is drawn for somebody's terminal.</summary>
    public bool ToTerminal { get; }

    /// <summary>
    /// Makes this the invocation for the calling flow until the returned
    /// handle is disposed.
    /// </summary>
    public IDisposable Enter()
    {
        var before = Ambient.Value;

        Ambient.Value = this;

        return new Leaving(before);
    }

    /// <summary>The directory a command was started in.</summary>
    public static string CurrentDirectory => Current?.WorkingDirectory ?? Directory.GetCurrentDirectory();

    /// <summary>
    /// Whether standard output is a pipe or a file rather than a terminal:
    /// inside an invocation, unless it is drawn for somebody's terminal.
    /// </summary>
    public static bool IsOutputRedirected => Current is { } current ? !current.ToTerminal : Console.IsOutputRedirected;

    /// <summary>
    /// Whether standard input is a pipe or a file rather than a terminal:
    /// always inside an invocation, which nobody can answer.
    /// </summary>
    public static bool IsInputRedirected => Current is not null || Console.IsInputRedirected;

    /// <summary>
    /// Points the process's standard streams at whichever invocation is
    /// writing, and at what they pointed to before for anything else.
    /// </summary>
    /// <remarks>
    /// Done once, by the host that runs commands in-process. The commands
    /// write <c>Console.Out</c> directly - the JSON document above all, which
    /// goes round the console so nothing can wrap it - and a call that is not
    /// part of any invocation, the daemon's own log among them, goes where it
    /// always went.
    /// </remarks>
    public static void RouteStandardStreams()
    {
        lock (Ambient)
        {
            // Routed again if something has pointed the streams elsewhere
            // since, as a test capturing the console does.
            if (_routed is not null && ReferenceEquals(Console.Out, _routed))
            {
                return;
            }

            var output = Console.Out;
            var error = Console.Error;
            var input = Console.In;

            Console.SetOut(new RoutingWriter(output, static current => current.Output));
            Console.SetError(new RoutingWriter(error, static current => current.Error));
            Console.SetIn(new RoutingReader(input));

            _routed = Console.Out;
        }
    }

    private sealed class Leaving(Invocation? before) : IDisposable
    {
        private int _done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) == 0)
            {
                Ambient.Value = before;
            }
        }
    }

    private sealed class RoutingWriter(TextWriter fallback, Func<Invocation, TextWriter> pick) : TextWriter
    {
        private TextWriter Target => Current is { } current ? pick(current) : fallback;

        public override Encoding Encoding => fallback.Encoding;

        public override IFormatProvider FormatProvider => fallback.FormatProvider;

        public override void Write(char value) => Target.Write(value);

        public override void Write(string? value) => Target.Write(value);

        public override void Write(char[] buffer, int index, int count) => Target.Write(buffer, index, count);

        public override void WriteLine(string? value) => Target.WriteLine(value);

        public override void WriteLine() => Target.WriteLine();

        public override Task WriteAsync(string? value)
        {
            Write(value);

            return Task.CompletedTask;
        }

        public override Task WriteLineAsync(string? value)
        {
            WriteLine(value);

            return Task.CompletedTask;
        }

        public override Task FlushAsync()
        {
            Flush();

            return Task.CompletedTask;
        }

        public override void Flush() => Target.Flush();
    }

    private sealed class RoutingReader(TextReader fallback) : TextReader
    {
        private TextReader Target => Current?.Input ?? fallback;

        public override int Peek() => Target.Peek();

        public override int Read() => Target.Read();

        public override int Read(char[] buffer, int index, int count) => Target.Read(buffer, index, count);

        public override string? ReadLine() => Target.ReadLine();

        public override string ReadToEnd() => Target.ReadToEnd();

        public override Task<string?> ReadLineAsync() => Target.ReadLineAsync();

        public override ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken) =>
            Target.ReadLineAsync(cancellationToken);

        public override Task<string> ReadToEndAsync() => Target.ReadToEndAsync();

        public override Task<string> ReadToEndAsync(CancellationToken cancellationToken) =>
            Target.ReadToEndAsync(cancellationToken);
    }
}
