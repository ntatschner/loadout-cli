<#
.SYNOPSIS
    Checks that a built MSI can upgrade over a running copy of the launcher.

.DESCRIPTION
    Guards one specific, expensive defect. Replacing loadout.exe while it is
    running requires something to close it first. Windows would normally do
    that itself through the Restart Manager, but the Restart Manager can be
    switched off machine-wide and on a hardened Windows build it often is:

      HKLM\SOFTWARE\Policies\Microsoft\Windows\Installer
        DisableAutomaticApplicationShutdown = 1

    Where it is off, the installer falls back to a dialog offering Retry or
    Cancel, retry cannot succeed because nothing closed the process in between,
    and the install ends in 1603 — indistinguishable, from outside, from the
    installer crashing. That shipped in 0.3.0 and cost somebody an evening.

    The package therefore closes the launcher itself, and it must do so before
    InstallValidate. That ordering is the whole fix and it is invisible: the
    package builds, installs and upgrades perfectly well on a machine where the
    Restart Manager is available, so nothing here would notice it regressing.
    Hence a check on the authoring rather than on a successful install.

    Reading the tables is non-destructive, so this can run anywhere. The
    optional live upgrade actually performs one, and is worth running before a
    release on a machine somebody can spare.

.PARAMETER Path
    The .msi to inspect.

.PARAMETER Live
    Also install the package over itself with the launcher running, which is
    the scenario that failed. Installs and uninstalls software: do not point it
    at a machine you are relying on.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Path,

    [switch] $Live
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not (Test-Path $Path)) { throw "No such package: $Path" }

$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $installer.GetType().InvokeMember(
    'OpenDatabase', 'InvokeMethod', $null, $installer, @((Resolve-Path $Path).Path, 0))

function Get-MsiRow {
    param([string] $Sql)

    $view = $database.GetType().InvokeMember('OpenView', 'InvokeMethod', $null, $database, @($Sql))
    $view.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $view, $null) | Out-Null

    while ($true) {
        $record = $view.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $view, $null)
        if ($null -eq $record) { break }

        $count = $record.GetType().InvokeMember('FieldCount', 'GetProperty', $null, $record, $null)

        $fields = for ($i = 1; $i -le $count; $i++) {
            [string] $record.GetType().InvokeMember('StringData', 'GetProperty', $null, $record, @($i))
        }

        # Wrapped so a single row is not unrolled into its own fields by the
        # pipeline, which would make a one-row answer look like several.
        Write-Output (, @($fields))
    }
}

$failures = New-Object System.Collections.Generic.List[string]

function Assert-That {
    param([bool] $Condition, [string] $Because)

    if ($Condition) { Write-Host "  ok    $Because" }
    else { Write-Host "  FAIL  $Because"; $failures.Add($Because) }
}

Write-Host "Checking $(Split-Path $Path -Leaf)"

$sequence = @{}
foreach ($row in Get-MsiRow 'SELECT `Action`,`Sequence` FROM `InstallExecuteSequence`') {
    $sequence[$row[0]] = [int] $row[1]
}

$close = $sequence.Keys | Where-Object { $_ -match 'CloseRunningLoadout|CloseApplications' } | Select-Object -First 1

Assert-That ($null -ne $close) 'the package has an action that closes the running launcher'

if ($close) {
    Assert-That ($sequence[$close] -lt $sequence['InstallValidate']) `
        "$close runs before InstallValidate, which is where a file in use stops the install"

    Assert-That ($sequence[$close] -lt $sequence['RemoveExistingProducts']) `
        "$close runs before RemoveExistingProducts, which deletes the old copy during an upgrade"
}

# An upgrade only replaces an older install when these agree, and a mismatch
# produces two entries in Add or Remove Programs rather than one upgraded one.
$upgrades = @(Get-MsiRow 'SELECT `UpgradeCode`,`ActionProperty` FROM `Upgrade`')
Assert-That ($upgrades.Count -ge 1) 'the package declares an upgrade relationship'
Assert-That ($null -ne ($upgrades | Where-Object { $_[1] -eq 'WIX_DOWNGRADE_DETECTED' })) `
    'a newer install is detected rather than silently overwritten'

if ($Live) {
    Write-Host 'Running the live upgrade check...'

    $exe = Join-Path $env:LOCALAPPDATA 'Programs\loadout\bin\loadout.exe'
    $log = Join-Path ([System.IO.Path]::GetTempPath()) 'loadout-verify-upgrade.log'

    & msiexec /i (Resolve-Path $Path).Path /quiet /norestart | Out-Null

    if (-not (Test-Path $exe)) { throw "The package did not install; nothing to upgrade over." }

    # Started so that its own file is held open, which is the state that broke.
    $running = Start-Process $exe -PassThru
    Start-Sleep -Seconds 4

    if ($running.HasExited) {
        throw 'The launcher exited immediately, so this would not have tested anything.'
    }

    $upgrade = Start-Process msiexec `
        -ArgumentList @('/i', (Resolve-Path $Path).Path, '/quiet', '/norestart', '/l*v', $log) -PassThru

    if (-not $upgrade.WaitForExit(180 * 1000)) {
        try { $upgrade.Kill() } catch { }
        Assert-That $false "the upgrade finished rather than waiting on a dialog (log: $log)"
    }
    else {
        Assert-That ($upgrade.ExitCode -eq 0) `
            "the upgrade succeeded with the launcher running (exit $($upgrade.ExitCode), log: $log)"
    }

    Get-Process loadout -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}

if ($failures.Count -gt 0) {
    throw "$($failures.Count) check(s) failed."
}

Write-Host 'All checks passed.'
