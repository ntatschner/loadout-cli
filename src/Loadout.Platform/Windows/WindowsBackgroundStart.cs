using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Platform.Windows;

/// <summary>
/// Starts a process on Windows that shares nothing with this one: no console,
/// no handles, and where Windows allows it, no job.
/// </summary>
/// <remarks>
/// <para>
/// Written against CreateProcess rather than through <see cref="Process"/>,
/// because Process.Start always lets the child inherit every inheritable
/// handle this process holds. When this process's output is a pipe, that
/// includes the pipe, and whatever is reading it - a script, the test suite -
/// waits for the end of a stream the daemon then holds open for as long as it
/// runs. Found by the contract test, which waited ninety seconds for a command
/// that had finished in two. Redirecting the child's own three streams does not
/// help: the handle is inherited whether or not it is one of the three.
/// </para>
/// <para>
/// Breaking away from the job matters for the same reason the rest does. A
/// terminal may put everything started in it into a job that is ended when
/// the terminal closes, and a daemon inside that job would go with the window
/// it was started from. Not every job allows it, and asking where it is not
/// allowed fails outright, so a refusal is followed by a start without it.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
internal static unsafe partial class WindowsBackgroundStart
{
    private const uint CreateNoWindow = 0x08000000;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint CreateBreakawayFromJob = 0x01000000;
    private const int AccessDenied = 5;

    internal static OperationResult<BackgroundProcess> Start(ProcessRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var line = CommandLine(request.Executable, request.Arguments);
        var environment = Block(request);

        if (!TryStart(line, environment, request.WorkingDirectory, CreateBreakawayFromJob, out var started, out var error)
            && error == AccessDenied
            && !TryStart(line, environment, request.WorkingDirectory, 0, out started, out error))
        {
            return Refused(request, error);
        }

        if (started is null)
        {
            return Refused(request, error);
        }

        return OperationResult<BackgroundProcess>.Ok(started);
    }

    private static OperationResult<BackgroundProcess> Refused(ProcessRequest request, int error) =>
        OperationResult<BackgroundProcess>.Fail(
            $"Could not start '{request.Executable}': {new System.ComponentModel.Win32Exception(error).Message}",
            ExitCode.GeneralFailure);

    private static bool TryStart(
        string line,
        string? environment,
        string? directory,
        uint extra,
        out BackgroundProcess? started,
        out int error)
    {
        started = null;
        error = 0;

        var startup = new StartupInfo { Size = sizeof(StartupInfo) };
        var flags = CreateNoWindow | extra | (environment is null ? 0 : CreateUnicodeEnvironment);

        // CreateProcess may write into the command line, so it is handed a
        // copy it owns rather than a string.
        var buffer = (line + '\0').ToCharArray();

        ProcessInformation information;
        bool created;

        fixed (char* command = buffer)
        fixed (char* block = environment)
        fixed (char* cwd = directory is { Length: > 0 } ? directory : null)
        {
            created = CreateProcess(
                null,
                command,
                nint.Zero,
                nint.Zero,
                inheritHandles: 0,
                flags,
                block,
                cwd,
                &startup,
                &information);
        }

        if (!created)
        {
            error = Marshal.GetLastPInvokeError();

            return false;
        }

        try
        {
            // Read while the handle is still held, so the identifier cannot
            // have been handed to anything else in between.
            using var process = Process.GetProcessById(information.ProcessId);

            started = new BackgroundProcess(information.ProcessId, process.StartTime.ToUniversalTime());

            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Gone already. Said as started, with no start time anybody can
            // match, which the caller reads as a daemon that died at once.
            started = new BackgroundProcess(information.ProcessId, DateTimeOffset.MinValue);

            return true;
        }
        finally
        {
            CloseHandle(information.Process);
            CloseHandle(information.Thread);
        }
    }

    /// <summary>
    /// One argument per argument, quoted the way the C runtime reads them back.
    /// </summary>
    /// <remarks>
    /// Backslashes are literal except before a quote, where they escape in
    /// pairs. Getting that wrong turns <c>C:\Program Files\</c> into a path
    /// that swallows the next argument.
    /// </remarks>
    internal static string CommandLine(string executable, IReadOnlyList<string> arguments)
    {
        var line = new StringBuilder();

        Append(line, executable);

        foreach (var argument in arguments)
        {
            line.Append(' ');
            Append(line, argument);
        }

        return line.ToString();
    }

    private static void Append(StringBuilder line, string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0)
        {
            line.Append(argument);

            return;
        }

        line.Append('"');

        for (var i = 0; ; i++)
        {
            var backslashes = 0;

            while (i < argument.Length && argument[i] == '\\')
            {
                i++;
                backslashes++;
            }

            if (i == argument.Length)
            {
                line.Append('\\', backslashes * 2);

                break;
            }

            if (argument[i] == '"')
            {
                line.Append('\\', (backslashes * 2) + 1);
                line.Append('"');
            }
            else
            {
                line.Append('\\', backslashes);
                line.Append(argument[i]);
            }
        }

        line.Append('"');
    }

    /// <summary>
    /// The environment to start with, or null to inherit this one as it is.
    /// </summary>
    private static string? Block(ProcessRequest request)
    {
        if (request.Environment is not { Count: > 0 } && request.RemoveEnvironmentPrefixes is not { Count: > 0 })
        {
            return null;
        }

        var variables = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            variables[(string)entry.Key] = (string?)entry.Value ?? string.Empty;
        }

        foreach (var prefix in request.RemoveEnvironmentPrefixes ?? [])
        {
            foreach (var name in variables.Keys.Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                variables.Remove(name);
            }
        }

        foreach (var (name, value) in request.Environment ?? new Dictionary<string, string>())
        {
            variables[name] = value;
        }

        var block = new StringBuilder();

        foreach (var (name, value) in variables)
        {
            block.Append(name).Append('=').Append(value).Append('\0');
        }

        return block.Append('\0').ToString();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public int Size;
        public nint Reserved;
        public nint Desktop;
        public nint Title;
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public int Columns;
        public int Rows;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2Size;
        public nint Reserved2;
        public nint StandardInput;
        public nint StandardOutput;
        public nint StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public nint Process;
        public nint Thread;
        public int ProcessId;
        public int ThreadId;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateProcess(
        char* application,
        char* commandLine,
        nint processAttributes,
        nint threadAttributes,
        int inheritHandles,
        uint creationFlags,
        char* environment,
        char* currentDirectory,
        StartupInfo* startupInfo,
        ProcessInformation* processInformation);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);
}
