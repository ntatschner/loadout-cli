using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Loadout.Tests.Contract;

/// <summary>
/// Records the state of this test host the first time a process it starts
/// exits 0xC0000142 with no output, and tries a few spawns of its own that
/// differ in one thing each.
/// </summary>
/// <remarks>
/// <para>
/// That exit code is a child that could not finish initialising, and once a
/// Windows CI leg sees it every later spawn in the run gets it too, for
/// fifteen minutes, with two threads and a serialised collection. A step run
/// after the job showed the session itself healthy: interactive desktop with
/// its whole heap, no desktop-heap event, no console-host crash, and both
/// probe spawns fine the moment the host had gone. So the condition lives in
/// this process and dies with it, and the only place to look at it is from
/// inside, at the moment it starts.
/// </para>
/// <para>
/// The variants are the diagnosis. A child that starts with an explicit
/// minimal environment but not with the inherited one says the host's
/// environment block is what broke; one that starts without a window but not
/// with one says it is the console the child would attach to; one that fails
/// every way says something below both. Written to a file the workflow
/// uploads and prints, and to standard error, so it survives whatever the
/// run does next.
/// </para>
/// </remarks>
internal static class SpawnRefusalProbe
{
    private static int _fired;

    internal static string FilePath => Path.Combine(AppContext.BaseDirectory, "spawn-refusal-probe.txt");

    /// <summary>Records once per process; later calls return at once.</summary>
    internal static void RecordOnce(string executable)
    {
        if (Interlocked.Exchange(ref _fired, 1) == 1)
        {
            return;
        }

        try
        {
            var report = Build(executable);

            File.WriteAllText(FilePath, report);
            Console.Error.WriteLine(report);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"spawn refusal probe failed: {ex}");
        }
    }

    internal static string Build(string executable)
    {
        var report = new StringBuilder();

        report.AppendLine("== spawn refusal probe");
        report.AppendLine($"time            : {DateTimeOffset.UtcNow:O}");
        report.AppendLine($"os              : {RuntimeInformation.OSDescription} {RuntimeInformation.OSArchitecture}");
        report.AppendLine($"process         : {Environment.ProcessId} {Environment.ProcessPath}");
        report.AppendLine($"threads         : {Process.GetCurrentProcess().Threads.Count}, handles {Process.GetCurrentProcess().HandleCount}");

        report.AppendLine("== working directory");
        var cwd = Environment.CurrentDirectory;
        report.AppendLine($"{cwd} exists={Directory.Exists(cwd)}");

        report.AppendLine("== environment block");
        var variables = Environment.GetEnvironmentVariables();
        report.AppendLine($"count           : {variables.Count}");

        foreach (var name in new[] { "SystemRoot", "windir", "PATH", "COMSPEC", "TEMP", "TMP", "USERPROFILE", "PATHEXT" })
        {
            var value = Environment.GetEnvironmentVariable(name);
            report.AppendLine($"{name,-15} : {(value is null ? "<unset>" : value.Length > 120 ? value[..120] + "…" : value)}");
        }

        var odd = 0;

        foreach (System.Collections.DictionaryEntry entry in variables)
        {
            var key = entry.Key?.ToString() ?? string.Empty;
            var value = entry.Value?.ToString() ?? string.Empty;

            if (key.Length == 0 || key.Contains('\0') || value.Contains('\0') || key.Contains('='))
            {
                odd++;
                report.AppendLine($"odd entry       : '{key.Replace("\0", "\\0")}'");
            }
        }

        report.AppendLine($"odd entries     : {odd}");

        if (OperatingSystem.IsWindows())
        {
            report.AppendLine("== console of this host");
            DescribeConsole(report);
        }

        report.AppendLine("== spawn variants (each is one change from how the suite starts a child)");
        Spawn(report, "inherited env, redirected", Shell(), configure: null);
        Spawn(report, "minimal explicit env, redirected", Shell(), configure: start =>
        {
            var keep = new[] { "SystemRoot", "windir", "PATH", "COMSPEC", "TEMP", "TMP", "USERPROFILE", "PATHEXT", "HOME" };
            var values = keep.ToDictionary(k => k, Environment.GetEnvironmentVariable);
            start.Environment.Clear();

            foreach (var (key, value) in values)
            {
                if (value is not null)
                {
                    start.Environment[key] = value;
                }
            }
        });
        Spawn(report, "inherited env, no window", Shell(), configure: start => start.CreateNoWindow = true);
        Spawn(report, "inherited env, nothing redirected", Shell(), configure: start =>
        {
            start.RedirectStandardInput = false;
            start.RedirectStandardOutput = false;
            start.RedirectStandardError = false;
        });
        Spawn(report, "the suite's own executable, --version", new ProcessStartInfo(executable) { ArgumentList = { "--version" } }, configure: null);

        return report.ToString();
    }

    private static ProcessStartInfo Shell() =>
        OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe") { ArgumentList = { "/c", "exit 7" } }
            : new ProcessStartInfo("/bin/sh") { ArgumentList = { "-c", "exit 7" } };

    private static void Spawn(StringBuilder report, string label, ProcessStartInfo start, Action<ProcessStartInfo>? configure)
    {
        start.UseShellExecute = false;
        start.RedirectStandardInput = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;

        configure?.Invoke(start);

        var watch = Stopwatch.StartNew();

        try
        {
            using var process = Process.Start(start)
                ?? throw new InvalidOperationException("Process.Start returned null");

            if (start.RedirectStandardInput)
            {
                process.StandardInput.Close();
            }

            var output = start.RedirectStandardOutput ? process.StandardOutput.ReadToEndAsync() : Task.FromResult(string.Empty);
            var error = start.RedirectStandardError ? process.StandardError.ReadToEndAsync() : Task.FromResult(string.Empty);

            if (!process.WaitForExit(15_000))
            {
                process.Kill(entireProcessTree: true);
                report.AppendLine($"{label,-40}: no exit within 15s");
                return;
            }

            var code = process.ExitCode;
            var stderr = error.Result.Trim();
            report.AppendLine($"{label,-40}: exit {code} (0x{code:X8}) in {watch.ElapsedMilliseconds}ms{(stderr.Length > 0 ? " stderr: " + stderr[..Math.Min(120, stderr.Length)] : string.Empty)}");
            _ = output;
        }
        catch (Exception ex)
        {
            report.AppendLine($"{label,-40}: threw {ex.GetType().Name}: {ex.Message}");
        }
    }

    [SupportedOSPlatform("windows")]
    private static void DescribeConsole(StringBuilder report)
    {
        var window = GetConsoleWindow();
        report.AppendLine($"console window  : {(window == 0 ? "none" : "0x" + window.ToString("X"))}");

        foreach (var (name, id) in new[] { ("stdin", -10), ("stdout", -11), ("stderr", -12) })
        {
            var handle = GetStdHandle(id);
            var type = GetFileType(handle);
            var mode = GetConsoleMode(handle, out var modeValue) ? $"console mode 0x{modeValue:X}" : $"not a console (GetConsoleMode error {Marshal.GetLastWin32Error()})";
            report.AppendLine($"{name,-15} : handle 0x{handle:X} type {FileType(type)}, {mode}");
        }

        report.AppendLine($"input redirected: {Console.IsInputRedirected}, output redirected: {Console.IsOutputRedirected}, error redirected: {Console.IsErrorRedirected}");

        // A console can exist without a window: a host started with
        // CreateNoWindow has one, and every child that does not ask for its
        // own attaches to it. GetConsoleCP is 0 only when there is none, and
        // the process list names the host and whatever else is attached.
        var codePage = GetConsoleCP();
        report.AppendLine($"console code page: {codePage} ({(codePage == 0 ? "no console" : "a console exists")})");

        var attached = new uint[64];
        var count = GetConsoleProcessList(attached, (uint)attached.Length);
        report.AppendLine($"attached procs  : {count}{(count > 0 ? " [" + string.Join(", ", attached.Take((int)Math.Min(count, attached.Length))) + "]" : string.Empty)}");

        var attachedToTerminal = Terminal.Gui.Drivers.Driver.IsAttachedToTerminal(out var inputAttached, out var outputAttached);
        report.AppendLine($"toolkit sees    : attached={attachedToTerminal} (input {inputAttached}, output {outputAttached})");
    }

    private static string FileType(uint type) => type switch
    {
        0x0001 => "disk",
        0x0002 => "char",
        0x0003 => "pipe",
        0x8000 => "remote",
        _ => $"unknown({type})",
    };

    [DllImport("kernel32.dll")]
    private static extern nint GetConsoleWindow();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetStdHandle(int handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetFileType(nint handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(nint handle, out uint mode);

    [DllImport("kernel32.dll")]
    private static extern uint GetConsoleCP();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetConsoleProcessList(uint[] processList, uint count);
}
