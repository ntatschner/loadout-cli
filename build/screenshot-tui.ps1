<#
.SYNOPSIS
    Photographs the launcher as it actually renders, in a real console.

.DESCRIPTION
    The test suite drives Terminal.Gui's ANSI driver and asserts on the text it
    produces. That proves what is on the screen; it cannot prove what it looks
    like. Spacing, colour against a real background, whether a column of
    identical badges reads as information or as noise, and whether the font has
    a glyph for the character being drawn are all invisible to it.

    The last of those is not a figure of speech. Terminal.Gui brackets every
    button in U+27E6 and U+27E7, which Cascadia Mono has no glyph for, so every
    button in the launcher rendered as a box on Windows for as long as the
    launcher has existed. The text was correct throughout. Nothing but a
    photograph was ever going to find it.

    Three things had to be worked out to make this reliable, all of them worth
    recording because each looked like it should have worked:

      * Windows Terminal cannot be used. It serves new windows from one
        long-running process and lets the hosted program overwrite the title,
        so a window it opens can be identified by neither process nor name.

      * SetForegroundWindow is refused to a process that is not already in the
        foreground. An earlier version of this called it, believed it, and
        quietly photographed whatever happened to be on top instead.

      * PrintWindow returns the frame and an empty black client area, because
        the console draws its text through DirectX.

    What works is to start the binary under a console host of its own, find the
    window by comparing the ConsoleWindowClass windows before and after, raise
    it without activating it, and read the screen.

.PARAMETER Exe
    The program to photograph. Defaults to this repository's debug build.

.PARAMETER Keys
    Keys to send before the photograph, so a screen reached by pressing
    something can be photographed too. Named keys go in braces — {DOWN} {F2}
    {F10} {CTRL-Q} — and anything else is typed a character at a time. See
    build/send-keys.ps1 for the full list.

    An earlier version of this said pressing a key was not supported, on the
    evidence that an injected arrow did nothing. That was wrong, and wrong in
    an instructive way: at the time an arrow genuinely did nothing at the
    filter, so the test could not tell a key that never arrived from a key
    that arrived and had nothing to do. Once the arrow was given a job, the
    same injection moved the list on the first try.

    Ctrl and a punctuation key is the one combination that really does not
    arrive. Ctrl+Q, the arrows, the function keys and every letter do.

.PARAMETER Glyphs
    Photograph a sheet of the characters Terminal.Gui decorates controls with,
    rather than the launcher. Anything drawn as a box is a character the
    console font cannot render and must not be used to convey anything.

.PARAMETER SettleMs
    How long to let the screen settle. The launcher reads every registered
    repository behind a splash, so this needs to outlast that.

.EXAMPLE
    ./build/screenshot-tui.ps1 -Out launcher.png

.EXAMPLE
    ./build/screenshot-tui.ps1 -Keys '{DOWN}','?' -Out keys.png
#>
[CmdletBinding()]
param(
    [string]   $Exe      = "$PSScriptRoot/../src/Loadout.Cli/bin/Debug/net10.0/loadout.exe",
    [string[]] $Keys     = @(),
    [string]   $Out      = 'launcher.png',
    [int]      $Cols     = 120,
    [int]      $Rows     = 34,
    [int]      $SettleMs = 8000,
    [switch]   $Glyphs
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not $IsWindows) {
    throw 'This photographs a Windows console. On Linux and macOS, capture the terminal emulator instead.'
}

Add-Type -AssemblyName System.Drawing

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class Shot
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    public delegate bool EnumProc(IntPtr h, IntPtr p);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(
        IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr SendMessageW(
        IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassNameW(IntPtr h, System.Text.StringBuilder n, int max);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(
        IntPtr h, int attr, out RECT r, int size);

    /// <summary>Every visible console window, by class rather than by name.</summary>
    public static List<IntPtr> Consoles()
    {
        var found = new List<IntPtr>();

        EnumWindows(delegate(IntPtr h, IntPtr p)
        {
            if (!IsWindowVisible(h)) { return true; }

            var name = new System.Text.StringBuilder(256);
            GetClassNameW(h, name, name.Capacity);

            if (name.ToString() == "ConsoleWindowClass") { found.Add(h); }

            return true;
        }, IntPtr.Zero);

        return found;
    }

    /// <summary>Brings a window to the front without taking the focus.</summary>
    public static void Raise(IntPtr h)
    {
        const uint NOMOVE = 0x0002, NOSIZE = 0x0001, SHOW = 0x0040, NOACTIVATE = 0x0010;

        SetWindowPos(h, new IntPtr(-1), 0, 0, 0, 0, NOMOVE | NOSIZE | SHOW | NOACTIVATE);
    }

    public static void Focus(IntPtr h) { SetForegroundWindow(h); }

    public static void Close(IntPtr h) { SendMessageW(h, 0x0010, IntPtr.Zero, IntPtr.Zero); }

    /// <summary>
    /// The visible frame, not GetWindowRect's, which includes the invisible
    /// resize border and would band the capture with the desktop behind it.
    /// </summary>
    public static RECT Frame(IntPtr h)
    {
        RECT r;

        if (DwmGetWindowAttribute(h, 9, out r, Marshal.SizeOf(typeof(RECT))) != 0)
        {
            throw new Exception("Could not measure the window.");
        }

        return r;
    }
}
'@

$scratch = Join-Path ([System.IO.Path]::GetTempPath()) 'loadout-screenshot'
New-Item -ItemType Directory -Force -Path $scratch | Out-Null

if ($Glyphs) {
    # The characters Terminal.Gui reaches for that fall outside the ranges a
    # stock console font covers. Rendering them is the only way to know which
    # of them the font can actually draw.
    $probe = Join-Path $scratch 'glyphs.ps1'

    @'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$g = [ordered]@{
  0x205E='VerticalFourDots'; 0x2219='Dot';          0x2261='IdenticalTo'
  0x2400='Null';             0x25C9='Selected';     0x25CB='UnSelected'
  0x2610='CheckUnChecked';   0x2611='CheckChecked'; 0x2630='File'
  0x2718='Close';            0x273D='Maximize';     0x274F='Minimize'
  0x2766='AppleBMP';         0x27E6='LeftBracket';  0x27E7='RightBracket'
  0x29C9='Copy';             0x2B1A='DottedSquare'; 0x2B1B='CheckNone'
  0xA909='Folder'
}
Write-Host "`n  A box means this font has no glyph for it.`n"
# Enumerated rather than indexed: an ordered dictionary given an integer looks
# up by position, not by key, so $g[0x27E6] asks for the 10214th entry.
foreach ($e in $g.GetEnumerator()) {
  Write-Host ("   {0}  U+{1:X4}  {2}" -f [char]::ConvertFromUtf32($e.Key), $e.Key, $e.Value)
}
Start-Sleep -Seconds 60
'@ | Set-Content -Path $probe -Encoding UTF8

    $launch = "pwsh -NoProfile -ExecutionPolicy Bypass -File `"$probe`""
    $SettleMs = [Math]::Min($SettleMs, 4000)
}
else {
    $Exe = (Resolve-Path $Exe).Path
    $launch = "`"$Exe`""
}

$leaf = if ($Glyphs) { 'pwsh' } else { [System.IO.Path]::GetFileNameWithoutExtension($Exe) }

$running = @(Get-Process $leaf -ErrorAction SilentlyContinue | ForEach-Object Id)

$before = [System.Collections.Generic.HashSet[IntPtr]]::new([Shot]::Consoles())

# mode sizes the console before the program reads it, so a photograph is of a
# known geometry rather than whatever this machine happens to default to.
Start-Process conhost.exe -ArgumentList @(
    'cmd.exe', '/c', "mode con: cols=$Cols lines=$Rows & $launch")

$deadline = [datetime]::UtcNow.AddSeconds(20)
$handle = [IntPtr]::Zero

while ($handle -eq [IntPtr]::Zero -and [datetime]::UtcNow -lt $deadline) {
    Start-Sleep -Milliseconds 250

    foreach ($h in [Shot]::Consoles()) {
        if (-not $before.Contains($h)) { $handle = $h; break }
    }
}

if ($handle -eq [IntPtr]::Zero) { throw 'No console window appeared.' }

[void][Shot]::ShowWindow($handle, 5)
[Shot]::Raise($handle)

Start-Sleep -Milliseconds $SettleMs

if ($Keys.Count -gt 0) {
    $app = Get-Process $leaf -ErrorAction SilentlyContinue |
        Where-Object { $_.Id -notin $running } |
        Select-Object -First 1

    if (-not $app) { throw "No $leaf process to send keys to." }

    & pwsh -NoProfile -ExecutionPolicy Bypass `
        -File (Join-Path $PSScriptRoot 'send-keys.ps1') `
        -Target $app.Id -Keys ($Keys -join [char]1)

    Start-Sleep -Milliseconds 1200
}

$r = [Shot]::Frame($handle)
$w = $r.Right - $r.Left
$h = $r.Bottom - $r.Top

$bitmap = New-Object System.Drawing.Bitmap $w, $h
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)

$graphics.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size $w, $h))
$bitmap.Save((Join-Path (Get-Location) $Out), [System.Drawing.Imaging.ImageFormat]::Png)

$graphics.Dispose()
$bitmap.Dispose()

[Shot]::Close($handle)

Write-Host "$Out  ${w}x${h}"
