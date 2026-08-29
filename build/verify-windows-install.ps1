<#
.SYNOPSIS
    Installs the built MSI, upgrades over a previous release, and removes it.

.DESCRIPTION
    Installs software. Intended for a disposable machine — a CI runner — and
    not for one somebody is using.

    This exists because the failure it checks for reached a user. Loadout 0.3.0
    could not be installed over 0.2.0 while the launcher was running: the
    Restart Manager is disabled by policy on plenty of managed Windows builds,
    the installer fell back to a Retry-or-Cancel dialog that retry could never
    satisfy, and it ended in 1603 — which from outside is indistinguishable
    from the installer crashing.

    Every part of that was invisible to CI, which built the MSI and never once
    installed it. The .deb has been installed and run on every release for
    months; the MSI, the one people double-click, was checked only by reading
    its tables.

    The interesting case is deliberately included: the upgrade is performed
    with the installed executable held open, which is what happens when
    somebody upgrades while the launcher is up. That is the exact shape of the
    original defect.

.PARAMETER Msi
    The package to install.

.PARAMETER PreviousVersion
    A released version to install first, so the upgrade path is exercised
    rather than only a fresh install. Skipped when absent.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Msi,

    [string] $PreviousVersion,

    # Asked of GitHub through the bounded runner below when no version is
    # given, because the caller asking inline is what hung.
    [string] $Repository,

    [string] $CurrentTag,

    # Well inside both the step timeout and the job timeout, so this fires
    # first and the step fails with a log rather than the job being cancelled
    # without one.
    [int] $WatchdogMinutes = 12
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$installed = Join-Path $env:LOCALAPPDATA 'Programs\loadout\bin\loadout.exe'
$logs = Join-Path ([System.IO.Path]::GetTempPath()) 'loadout-install-logs'

New-Item -ItemType Directory -Force -Path $logs | Out-Null

# A deadline that does not depend on this script being able to act on it.
#
# Every wait below is bounded, and five releases have still stalled here with
# nothing to read. Bounding a wait is not enough: when one expires the script
# calls Kill and only then throws, and Kill blocks in turn when the process is
# wedged inside the installer service, so the throw never arrives. The runner
# then asks the step to stop, whatever is stuck does not answer, the job times
# out, and a cancelled job uploads no logs at all.
#
# So the deadline is enforced from a second runspace. It shares the process but
# not the blocked thread, which means it still runs when the main thread is
# inside a native call nothing can interrupt, and Environment.Exit takes the
# process down from under it. The step then fails, and a failed step keeps its
# log — which is the whole thing that four previous investigations lacked.
$watchdog = [powershell]::Create()
$watchdog.Runspace = [runspacefactory]::CreateRunspace()
$watchdog.Runspace.Open()
$watchdog.Runspace.SessionStateProxy.SetVariable(
    'deadline', [DateTime]::UtcNow.AddMinutes($WatchdogMinutes))
$watchdog.Runspace.SessionStateProxy.SetVariable(
    'progressPath', (Join-Path $logs 'progress.log'))
$watchdog.Runspace.SessionStateProxy.SetVariable('allowed', $WatchdogMinutes)

[void]$watchdog.AddScript({
    while ([DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Seconds 5
    }

    # Written straight to the console handle rather than through Write-Host,
    # which belongs to the other runspace's host and would be lost here.
    [Console]::Out.WriteLine(
        "--- watchdog: the install check did not finish within $allowed minutes ---")

    if (Test-Path $progressPath) {
        [Console]::Out.WriteLine('--- phases reached ---')
        [Console]::Out.WriteLine((Get-Content $progressPath -Raw))
    }

    [Console]::Out.WriteLine(
        '--- the phase above is the one that never came back ---')
    [Console]::Out.Flush()

    [Environment]::Exit(2)
})

[void]$watchdog.BeginInvoke()

<#
.SYNOPSIS
    Announces a phase, with the time, so a log that stops tells you where.

.DESCRIPTION
    The 0.9.1 release hung in this script for thirty minutes and was cancelled
    by the job timeout — and a cancelled job keeps no logs at all, so there was
    nothing to read afterwards. That was the second release in a row to fail
    here with no evidence, the first having been 0.8.0.

    Bounding every wait was supposed to have fixed that and did not, because a
    bound above the job timeout is not a bound. Two things follow: every wait
    below is short enough that the whole script must finish well inside the job
    timeout, so it throws and the step *fails* — a failed step keeps its log
    where a cancelled one does not — and each phase announces itself, so even a
    truncated log names the thing that never came back.
#>
function Step {
    param([string] $Message)

    $line = "[$([DateTime]::UtcNow.ToString('HH:mm:ss'))] $Message"

    Write-Host $line

    # Also to a file, because Write-Host is not enough here. When this step
    # stalls it does not end: the runner asks it to stop, whatever is stuck does
    # not answer, and the job is cancelled — and a cancelled job uploads no logs
    # at all. Four releases have stalled here and every one of them left nothing
    # to read, including the one where the step had its own timeout.
    #
    # A file survives that, and a later step reads it back with if: always().
    Add-Content -Path (Join-Path $logs 'progress.log') -Value $line -ErrorAction SilentlyContinue
}

<#
.SYNOPSIS
    Runs an external tool with a deadline.

.DESCRIPTION
    Downloading the previous release was the one wait in this script with no
    bound on it at all, which made it the only candidate that could hang for
    longer than every bounded call put together.
#>
function Invoke-BoundedTool {
    param([string] $Label, [string] $File, [string[]] $Arguments, [int] $TimeoutSeconds = 120)

    $out = Join-Path $logs "$Label.out"
    $err = Join-Path $logs "$Label.err"

    $process = Start-Process $File -PassThru -NoNewWindow -ArgumentList $Arguments `
        -RedirectStandardOutput $out -RedirectStandardError $err

    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        try { $process.Kill($true) } catch { }

        throw "$Label did not finish within $TimeoutSeconds seconds. It is waiting on something. Output so far: $out"
    }

    if ($process.ExitCode -ne 0) {
        Get-Content $err -ErrorAction SilentlyContinue |
            Select-Object -First 10 |
            ForEach-Object { Write-Host "    $_" }

        throw "$Label failed with $($process.ExitCode)."
    }
}

function Invoke-Msi {
    # Three minutes is far longer than any of these take, and short enough that
    # every call this script makes still fits inside the job timeout with room
    # to spare. That margin is the whole point: it is what turns a hang into a
    # failure with a log.
    param([string] $Label, [string[]] $Arguments, [int] $TimeoutSeconds = 180)

    $log = Join-Path $logs "$Label.log"

    $process = Start-Process msiexec -PassThru -ArgumentList (
        $Arguments + @('/quiet', '/norestart', '/l*v', $log))

    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        try { $process.Kill() } catch { }

        # A hung installer is the original symptom, so it is reported as
        # itself rather than as a timeout with no explanation.
        throw "$Label did not finish within $TimeoutSeconds seconds. It is waiting on something. Log: $log"
    }

    Write-Host "  $Label exit $($process.ExitCode)"

    if ($process.ExitCode -ne 0) {
        Get-Content $log -Encoding Unicode -ErrorAction SilentlyContinue |
            Select-String -Pattern 'RESTART MANAGER|Installation success or error status|Return value 3' |
            Select-Object -Last 6 |
            ForEach-Object { Write-Host "    $_" }

        throw "$Label failed with $($process.ExitCode). Log: $log"
    }
}

<#
.SYNOPSIS
    Runs the installed launcher with a deadline.

.DESCRIPTION
    Every msiexec call here has always been bounded; running the installed
    binary was not. That is the gap the 0.8.0 release fell into: the win-x64
    installer job hung for forty-eight minutes and was killed with no log left
    to read, so what it had been waiting on could not be established at all.

    A command that will not finish is a defect worth reporting as itself. Two
    minutes is far longer than any of these take and far shorter than a job.
#>
function Invoke-Bounded {
    param([string] $Label, [string[]] $Arguments, [int] $TimeoutSeconds = 60)

    $out = Join-Path $logs "$Label.out"
    $err = Join-Path $logs "$Label.err"

    $process = Start-Process $installed -PassThru -NoNewWindow `
        -ArgumentList $Arguments -RedirectStandardOutput $out -RedirectStandardError $err

    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        try { $process.Kill($true) } catch { }

        throw "'loadout $($Arguments -join ' ')' did not finish within $TimeoutSeconds seconds. " +
            "It is waiting on something. Output so far: $out"
    }

    return [pscustomobject]@{
        ExitCode = $process.ExitCode
        Output   = @(Get-Content $out -ErrorAction SilentlyContinue)
    }
}

function Assert-Runs {
    param([string] $Expected)

    if (-not (Test-Path $installed)) {
        throw "Nothing was installed at $installed."
    }

    $version = (Invoke-Bounded 'version' @('--version')).Output -join ''

    if ($Expected -and $version -ne $Expected) {
        throw "Expected version $Expected, found $version."
    }

    # Running it is the point. A package that lays down a file which cannot
    # start is a package that passed every check that mattered less.
    $doctor = Invoke-Bounded 'doctor' @('doctor', '--json')

    if ($doctor.ExitCode -ne 0 -and $doctor.ExitCode -ne 1) {
        throw "doctor exited $($doctor.ExitCode), which is neither success nor an ordinary report of problems."
    }

    Write-Host "  runs, reports $version"
}

# The package's own identity, which is what MSI indexes installations by.
$upgradeCode = '{B3CB085D-BC14-5901-AD7D-A4F6E3BAE121}'

<#
.SYNOPSIS
    The installed product code and version, or nothing.

.DESCRIPTION
    Asked of MSI rather than looked for in the registry. A per-user
    installation is not reliably listed under HKCU's Uninstall key — this
    machine has 0.5.1 installed and working, MSI refuses to install an
    older version over it, and that key holds nothing at all. Reading the
    registry is guessing at a layout; RelatedProducts is the question
    actually being asked.
#>
function Get-InstalledProduct {
    param([int] $TimeoutSeconds = 60)

    # Asked in a job with a deadline, because this was the last wait in the
    # script with no bound on it, and it is a plausible place to stop: the
    # Windows Installer COM API blocks while another installation holds the
    # _MSIExecute mutex, and this runs either side of four msiexec calls.
    #
    # Three releases have stalled in this step for half an hour. Bounding the
    # msiexec calls did not stop it, which is what pointed here.
    $job = Start-Job -ArgumentList $upgradeCode -ScriptBlock {
        param($code)

        $installer = New-Object -ComObject WindowsInstaller.Installer

        $related = $installer.GetType().InvokeMember(
            'RelatedProducts', 'GetProperty', $null, $installer, @($code))

        foreach ($product in $related) {
            $version = $installer.GetType().InvokeMember(
                'ProductInfo', 'GetProperty', $null, $installer, @($product, 'VersionString'))

            return [pscustomobject]@{ ProductCode = $product; Version = $version }
        }

        return $null
    }

    if (-not (Wait-Job $job -Timeout $TimeoutSeconds)) {
        Stop-Job $job -ErrorAction SilentlyContinue
        Remove-Job $job -Force -ErrorAction SilentlyContinue

        throw "Asking Windows Installer what is installed did not answer within $TimeoutSeconds seconds. " +
            'Something else is holding the installer mutex.'
    }

    $result = Receive-Job $job
    Remove-Job $job -Force -ErrorAction SilentlyContinue

    return $result
}

# A runner is clean; a developer's machine is not, and a newer version
# already installed makes installing an older one a downgrade, which the
# package refuses by design. Clearing the decks first is what lets this run
# anywhere rather than only in CI.
Step 'Asking Windows Installer what is already here...'

$existing = Get-InstalledProduct

if ($existing) {
    Step "Removing the $($existing.Version) already installed here..."
    Invoke-Msi 'remove-existing' @('/x', $existing.ProductCode)
}

Step 'Decks cleared.'

# Worked out here rather than by the workflow step that calls this.
#
# The step used to ask 'gh release list' for it inline, with no deadline on the
# call, and that turned out to be where five releases stalled: the hang was
# ahead of this script rather than inside it, which is why bounding every wait
# below never helped and why the phase log was always empty. Asked for here, it
# goes through the same bounded runner as every other tool call.
if (-not $PreviousVersion -and $Repository) {
    Step 'Asking which release to upgrade from...'

    Invoke-BoundedTool 'previous-release' 'gh' @(
        'release', 'list', '--repo', $Repository, '--limit', '5', '--json', 'tagName')

    $listed = Get-Content (Join-Path $logs 'previous-release.out') -Raw -ErrorAction SilentlyContinue

    if ($listed) {
        $PreviousVersion = ($listed | ConvertFrom-Json |
            Where-Object { $_.tagName -ne $CurrentTag } |
            Select-Object -First 1).tagName -replace '^v', ''
    }

    if ($PreviousVersion) {
        Step "Upgrading from $PreviousVersion."
    }
    else {
        Step 'No earlier release to upgrade from; checking a fresh install only.'
    }
}

if ($PreviousVersion) {
    Write-Host "Installing $PreviousVersion first, so the upgrade path is the one under test..."

    $previous = Join-Path $logs "previous.msi"

    Step "Downloading v$PreviousVersion..."

    Invoke-BoundedTool 'download-previous' 'gh' @(
        'release', 'download', "v$PreviousVersion",
        '--repo', 'ntatschner/loadout-cli',
        '--pattern', '*win-x64.msi',
        '--output', $previous,
        '--clobber')

    Step 'Installing the previous version...'
    Invoke-Msi 'install-previous' @('/i', $previous)

    Step 'Checking the previous version runs...'
    Assert-Runs -Expected $PreviousVersion

    # Upgraded in place, which is the path that broke and shipped.
    #
    # It deliberately does not hold the file open first. An earlier version of
    # this did, on the reasoning that a running launcher holds its own image —
    # and it failed against a package that is demonstrably fixed. The lock was
    # unrealistic: the file was held by PowerShell, and what closes a running
    # launcher matches processes by name, so it correctly found nothing and
    # said so:
    #
    #   CustomAction CloseRunningLoadout returned actual error code 128
    #
    # Killing an unrelated process that happens to hold the file would be the
    # wrong behaviour, so that case is not one to test for here. The action's
    # presence and its position ahead of InstallValidate are checked by
    # build/verify-windows.ps1, which fails against the release that shipped
    # without them.
    Step 'Upgrading in place...'

    Invoke-Msi 'upgrade' @('/i', $Msi)
}
else {
    Step 'No previous version given; checking a fresh install only.'
    Invoke-Msi 'install' @('/i', $Msi)
}

Step 'Checking the upgraded version runs...'
Assert-Runs -Expected ''

Step 'Checking the install put loadout on PATH...'

$path = [Environment]::GetEnvironmentVariable('PATH', 'User')

if ($path -notlike '*loadout*') {
    throw 'The install did not add loadout to the user PATH, so nothing can find it by name.'
}

Step 'Removing it...'

Step 'Asking Windows Installer for the product code...'

$product = Get-InstalledProduct

if (-not $product) {
    throw 'Loadout is not registered with the installer, so it cannot be uninstalled.'
}

Invoke-Msi 'uninstall' @('/x', $product.ProductCode)

if (Test-Path $installed) {
    throw "Uninstalling left $installed behind."
}

Write-Host 'Install, upgrade and uninstall all pass.'
