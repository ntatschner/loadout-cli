<#
.SYNOPSIS
    Reads the text out of another process's console screen buffer.

.DESCRIPTION
    What the application actually drew, in a real terminal, with the real
    driver — as text rather than as pixels.

    Worth having alongside the screenshot script, and for most purposes better
    than it. A photograph has to be looked at; this can be checked. It also
    works when the screen is locked, which a photograph does not: a capture
    taken after the machine locked came back as the desktop wallpaper.

    Read it once, or a few times at most. Reading another process's console
    means attaching to that console, and doing it repeatedly disturbs the
    application being watched: polling every 300ms for a minute left the
    launcher drawn but with an empty project list, and keys sent afterwards
    went to the wrong view. Every run where keys were delivered correctly was
    a run that had not polled first.

    So the two scripts do not mix. Use send-keys.ps1 with a fixed wait when
    driving the application, and use this when the question is only what is on
    the screen. Startup varies from about a second to over twenty depending on
    how the repositories read, so a fixed wait has to be generous.

.PARAMETER Target
    Process id of a process attached to the console to be read.
#>
[CmdletBinding()]
param([Parameter(Mandatory)] [int] $Target)

$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class Screen
{
    [StructLayout(LayoutKind.Sequential)] public struct COORD { public short X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct SMALL_RECT { public short Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct CONSOLE_SCREEN_BUFFER_INFO
    {
        public COORD Size;
        public COORD CursorPosition;
        public ushort Attributes;
        public SMALL_RECT Window;
        public COORD MaximumWindowSize;
    }

    [DllImport("kernel32.dll")] public static extern bool FreeConsole();
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool AttachConsole(uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateFileW(string name, uint access, uint share,
        IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll")] public static extern bool GetConsoleScreenBufferInfo(
        IntPtr handle, out CONSOLE_SCREEN_BUFFER_INFO info);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern bool ReadConsoleOutputCharacterW(
        IntPtr handle, StringBuilder text, uint length, COORD at, out uint read);

    public static string Read(uint pid)
    {
        FreeConsole();

        if (!AttachConsole(pid))
        {
            throw new Exception("Could not attach: " + Marshal.GetLastWin32Error());
        }

        IntPtr conout = CreateFileW("CONOUT$", 0x80000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);

        if (conout == new IntPtr(-1))
        {
            throw new Exception("CONOUT$ would not open: " + Marshal.GetLastWin32Error());
        }

        CONSOLE_SCREEN_BUFFER_INFO info;

        if (!GetConsoleScreenBufferInfo(conout, out info))
        {
            throw new Exception("Could not measure the buffer.");
        }

        var lines = new StringBuilder();
        var width = (uint)info.Size.X;

        // Only the visible window, not the whole scrollback: what is on the
        // screen is the question being asked.
        for (short row = info.Window.Top; row <= info.Window.Bottom; row++)
        {
            var line = new StringBuilder((int)width + 1);
            uint read;

            ReadConsoleOutputCharacterW(conout, line, width, new COORD { X = 0, Y = row }, out read);

            lines.AppendLine(line.ToString().TrimEnd());
        }

        return lines.ToString();
    }
}
'@

[Screen]::Read([uint32]$Target)
