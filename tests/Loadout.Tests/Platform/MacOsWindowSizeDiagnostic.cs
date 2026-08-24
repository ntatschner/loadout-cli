using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Loadout.Models.Platform;
using Loadout.Platform.Abstractions;
using Loadout.Platform.Unix;
using Xunit;

namespace Loadout.Tests.Platform;

/// <summary>
/// A temporary measurement, not a test.
/// <para>
/// Two window-size tests fail on macOS and pass on Linux, and no macOS machine
/// is available to debug on. The symptoms rule out the obvious causes on their
/// own — the resize call returns success and the child still sees nothing — so
/// this reports what the operating system actually does rather than what it is
/// assumed to do. It is deleted once the cause is known.
/// </para>
/// </summary>
public sealed class MacOsWindowSizeDiagnostic
{
    [Fact]
    [SupportedOSPlatform("macos")]
    public async Task Report_what_the_window_size_calls_actually_do()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var report = new List<string>
        {
            $"request=0x{NativeTerminal.SetWindowSize:X}",
            $"sizeof={Marshal.SizeOf<NativeTerminal.WindowSize>()}",
        };

        using var terminal = new UnixPseudoTerminal();

        var started = await terminal.StartAsync(
            new ProcessRequest("/bin/sh", ["-c", "stty size; sleep 1; stty size"]),
            columns: 132,
            rows: 43);

        report.Add($"start={started.Succeeded}/{started.Error ?? "-"}");

        var resize = terminal.Resize(140, 50);

        report.Add($"resize={resize.Succeeded}/{resize.Error ?? "-"}");
        report.Add($"child=[{(await DrainAsync(terminal)).ReplaceLineEndings(" | ")}]");

        // Deliberately failing: this is how the numbers reach the CI log, which
        // is the only macOS machine this project has.
        Assert.Fail("MACOS-DIAG " + string.Join(" ;; ", report));
    }

    private static async Task<string> DrainAsync(IPseudoTerminal terminal)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var buffer = new byte[4096];
        var builder = new StringBuilder();

        while (true)
        {
            var read = await terminal.ReadAsync(buffer, cancellation.Token);

            if (read.Failed || read.Value == 0)
            {
                return builder.ToString();
            }

            builder.Append(Encoding.UTF8.GetString(buffer, 0, read.Value));
        }
    }
}
