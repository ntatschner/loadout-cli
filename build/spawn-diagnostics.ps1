<#
.SYNOPSIS
Records what the session looks like when a Windows test leg has just failed
spawning processes with 0xC0000142.

.DESCRIPTION
0xC0000142 (STATUS_DLL_INIT_FAILED) with no output is a console process that
could not finish initialising, which on Windows means it could not connect to
its console or to the desktop: desktop heap exhausted, or the console host it
must attach to broken. Both are conditions of the session rather than of the
process being started, which is why, once it starts, every later spawn in the
run fails whatever the concurrency. The runs that showed it left no evidence of
which, because the suite's own log only carries exit codes.

This prints the things that tell those apart, and a live probe that says
whether the condition outlived the test host. It changes nothing.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Continue'

function Heading([string] $text) {
    Write-Host ''
    Write-Host "== $text"
}

Heading 'Session'
$me = Get-Process -Id $PID
Write-Host "session id      : $($me.SessionId)"
Write-Host "user interactive: $([Environment]::UserInteractive)"
Write-Host "windows version : $([Environment]::OSVersion.VersionString)"

Add-Type -Namespace Probe -Name User32 -MemberDefinition @'
[DllImport("user32.dll", SetLastError = true)]
public static extern IntPtr GetProcessWindowStation();
[DllImport("user32.dll", SetLastError = true)]
public static extern IntPtr GetThreadDesktop(uint threadId);
[DllImport("kernel32.dll")]
public static extern uint GetCurrentThreadId();
[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
public static extern bool GetUserObjectInformation(IntPtr handle, int index, System.Text.StringBuilder info, int length, out int needed);
[DllImport("user32.dll", SetLastError = true)]
public static extern bool GetUserObjectInformation(IntPtr handle, int index, out uint info, int length, out int needed);
'@

try {
    $name = New-Object System.Text.StringBuilder 256
    $needed = 0
    $station = [Probe.User32]::GetProcessWindowStation()
    [void][Probe.User32]::GetUserObjectInformation($station, 2, $name, 256, [ref] $needed)   # UOI_NAME
    Write-Host "window station  : $($name.ToString())"

    $desktop = [Probe.User32]::GetThreadDesktop([Probe.User32]::GetCurrentThreadId())
    $name = New-Object System.Text.StringBuilder 256
    [void][Probe.User32]::GetUserObjectInformation($desktop, 2, $name, 256, [ref] $needed)
    $heap = [uint32] 0
    [void][Probe.User32]::GetUserObjectInformation($desktop, 5, [ref] $heap, 4, [ref] $needed)  # UOI_HEAPSIZE, in KB
    Write-Host "desktop         : $($name.ToString()), heap ${heap} KB"
} catch {
    Write-Host "window station lookup failed: $($_.Exception.Message)"
}

Heading 'Processes that hold consoles or were spawned by the suite'
Get-Process -Name conhost, OpenConsole, dotnet, loadout, cmd, pwsh, powershell, git, testhost -ErrorAction SilentlyContinue |
    Group-Object Name |
    Sort-Object Name |
    ForEach-Object { Write-Host ("{0,-12} {1}" -f $_.Name, $_.Count) }

Heading 'Desktop heap allocation failures (System, Win32k event 243, last 24 hours)'
$since = (Get-Date).AddHours(-24)
$heapEvents = Get-WinEvent -FilterHashtable @{ LogName = 'System'; Id = 243; StartTime = $since } -ErrorAction SilentlyContinue
if ($heapEvents) {
    $heapEvents | ForEach-Object { Write-Host "$($_.TimeCreated) $($_.ProviderName): $($_.Message -replace '\s+', ' ')" }
} else {
    Write-Host 'none'
}

Heading 'Application crashes naming a console host or a suite process (events 1000/1001, last 24 hours)'
$crashes = Get-WinEvent -FilterHashtable @{ LogName = 'Application'; Id = 1000, 1001; StartTime = $since } -ErrorAction SilentlyContinue |
    Where-Object { $_.Message -match 'conhost|OpenConsole|dotnet|loadout|testhost|cmd\.exe|pwsh' }
if ($crashes) {
    $crashes | Select-Object -First 20 | ForEach-Object { Write-Host "$($_.TimeCreated) $($_.ProviderName): $(($_.Message -replace '\s+', ' ').Substring(0, [Math]::Min(300, $_.Message.Length)))" }
} else {
    Write-Host 'none'
}

Heading 'Live probe: can this session still start a console process?'
$probe = Start-Process -FilePath 'cmd.exe' -ArgumentList '/c', 'exit 0' -NoNewWindow -Wait -PassThru
Write-Host "cmd.exe /c exit 0 -> exit code $($probe.ExitCode)"

$built = Get-ChildItem -Path 'src/Loadout.Cli/bin/Release' -Recurse -Filter 'loadout.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
if ($built) {
    $probe = Start-Process -FilePath $built.FullName -ArgumentList '--version' -NoNewWindow -Wait -PassThru
    Write-Host "loadout --version -> exit code $($probe.ExitCode)"
} else {
    Write-Host 'no built loadout.exe to probe with'
}

exit 0
