<#
.SYNOPSIS
    Sends keystrokes to a console application running in another process.

.DESCRIPTION
    Writes key events into the target console's input buffer, which is what a
    console application actually reads.

    SendKeys does nothing at all here, and it is worth saying why: it posts
    window messages, and a console reads its input buffer rather than its
    messages. An earlier version of the screenshot script used it, believed it,
    and quietly photographed a screen nothing had been typed into.

    Both halves matter. A key event carries a virtual key code, a scan code and
    a character, and different keys are recognised by different parts of it —
    a letter by its character, an arrow or a function key by its code. Sending
    one without the other gets a key that arrives and does nothing.

    Verified against the launcher in a real console: arrows move the list,
    letters reach the filter, F10 opens the menu, F2 opens settings, and
    Ctrl+Q quits. Ctrl and a punctuation key does not work — Ctrl+comma
    arrives and is ignored — which is a property of the toolkit rather than of
    this script, and the reason the launcher does not use one.

.PARAMETER Target
    Process id of the application to type into. Any process attached to the
    console will do; the one running the program is the obvious choice.

.PARAMETER Keys
    What to send, one entry per key or per run of text. Named keys are written
    in braces: {DOWN} {UP} {LEFT} {RIGHT} {ENTER} {TAB} {ESC} {SPACE}
    {F1} {F2} {F3} {F9} {F10}, and {CTRL-P} {CTRL-N} {CTRL-Q}. Anything else
    is typed a character at a time.

.EXAMPLE
    ./build/send-keys.ps1 -Target 1234 -Keys '{DOWN}','j','?'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [int] $Target,
    [Parameter(Mandatory)] [string[]] $Keys
)

$ErrorActionPreference = 'Stop'

# pwsh -File flattens an array into one argument, so a caller invoking this as
# a file joins the keys with a character no key contains and they are split
# back here. Calling it in-process with a real array works as it reads.
if ($Keys.Count -eq 1 -and $Keys[0].Contains([char]1)) {
    $Keys = $Keys[0].Split([char]1)
}

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class Keystrokes
{
    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT_RECORD
    {
        public ushort EventType;
        public uint   KeyDown;
        public ushort RepeatCount;
        public ushort VirtualKeyCode;
        public ushort VirtualScanCode;
        public ushort UnicodeChar;
        public uint   ControlKeyState;
    }

    [DllImport("kernel32.dll")] public static extern bool FreeConsole();
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool AttachConsole(uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateFileW(string name, uint access, uint share,
        IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll")] public static extern bool WriteConsoleInputW(
        IntPtr handle, INPUT_RECORD[] records, uint count, out uint written);
    [DllImport("user32.dll")] public static extern uint MapVirtualKeyW(uint code, uint type);

    public static void Send(uint pid, ushort[] codes, ushort[] chars, uint[] control)
    {
        // Detached first: a process can only be attached to one console, and
        // this one has its own.
        FreeConsole();

        if (!AttachConsole(pid))
        {
            throw new Exception("Could not attach to that console: " + Marshal.GetLastWin32Error());
        }

        IntPtr input = CreateFileW("CONIN$", 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);

        if (input == new IntPtr(-1))
        {
            throw new Exception("CONIN$ would not open: " + Marshal.GetLastWin32Error());
        }

        for (int i = 0; i < codes.Length; i++)
        {
            // Down and up. A key that is never released is a key half pressed.
            var records = new INPUT_RECORD[2];

            for (int half = 0; half < 2; half++)
            {
                records[half].EventType = 1;                  // KEY_EVENT
                records[half].KeyDown = half == 0 ? 1u : 0u;
                records[half].RepeatCount = 1;
                records[half].VirtualKeyCode = codes[i];
                records[half].VirtualScanCode = (ushort)MapVirtualKeyW(codes[i], 0);
                records[half].UnicodeChar = chars[i];
                records[half].ControlKeyState = control[i];
            }

            uint written;
            WriteConsoleInputW(input, records, 2, out written);
        }
    }
}
'@

$named = @{
    '{DOWN}'  = 0x28; '{UP}'    = 0x26; '{LEFT}' = 0x25; '{RIGHT}' = 0x27
    '{ENTER}' = 0x0D; '{TAB}'   = 0x09; '{ESC}'  = 0x1B; '{SPACE}' = 0x20
    '{F1}'    = 0x70; '{F2}'    = 0x71; '{F3}'   = 0x72; '{F9}'    = 0x78
    '{F10}'   = 0x79
}

$withControl = @{
    '{CTRL-P}' = 0x50; '{CTRL-N}' = 0x4E; '{CTRL-Q}' = 0x51
}

$codes   = New-Object System.Collections.Generic.List[uint16]
$chars   = New-Object System.Collections.Generic.List[uint16]
$control = New-Object System.Collections.Generic.List[uint32]

foreach ($key in $Keys) {
    if ($withControl.ContainsKey($key)) {
        $codes.Add([uint16]$withControl[$key])
        $chars.Add([uint16]0)
        $control.Add([uint32]0x0008)          # LEFT_CTRL_PRESSED
    }
    elseif ($named.ContainsKey($key)) {
        $codes.Add([uint16]$named[$key])
        $chars.Add([uint16]0)
        $control.Add([uint32]0)
    }
    else {
        foreach ($c in $key.ToCharArray()) {
            $codes.Add([uint16][char]::ToUpper($c))
            $chars.Add([uint16]$c)
            $control.Add([uint32]0)
        }
    }
}

[Keystrokes]::Send([uint32]$Target, $codes.ToArray(), $chars.ToArray(), $control.ToArray())
