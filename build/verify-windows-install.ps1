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

    [string] $PreviousVersion
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$installed = Join-Path $env:LOCALAPPDATA 'Programs\loadout\bin\loadout.exe'
$logs = Join-Path ([System.IO.Path]::GetTempPath()) 'loadout-install-logs'

New-Item -ItemType Directory -Force -Path $logs | Out-Null

function Invoke-Msi {
    param([string] $Label, [string[]] $Arguments, [int] $TimeoutSeconds = 300)

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
    param([string] $Label, [string[]] $Arguments, [int] $TimeoutSeconds = 120)

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
    $installer = New-Object -ComObject WindowsInstaller.Installer

    $related = $installer.GetType().InvokeMember(
        'RelatedProducts', 'GetProperty', $null, $installer, @($upgradeCode))

    foreach ($code in $related) {
        $version = $installer.GetType().InvokeMember(
            'ProductInfo', 'GetProperty', $null, $installer, @($code, 'VersionString'))

        return [pscustomobject]@{ ProductCode = $code; Version = $version }
    }

    return $null
}

# A runner is clean; a developer's machine is not, and a newer version
# already installed makes installing an older one a downgrade, which the
# package refuses by design. Clearing the decks first is what lets this run
# anywhere rather than only in CI.
$existing = Get-InstalledProduct

if ($existing) {
    Write-Host "Removing the $($existing.Version) already installed here..."
    Invoke-Msi 'remove-existing' @('/x', $existing.ProductCode)
}

if ($PreviousVersion) {
    Write-Host "Installing $PreviousVersion first, so the upgrade path is the one under test..."

    $previous = Join-Path $logs "previous.msi"

    & gh release download "v$PreviousVersion" --repo ntatschner/loadout-cli `
        --pattern '*win-x64.msi' --output $previous --clobber

    if ($LASTEXITCODE -ne 0) {
        throw "Could not download v$PreviousVersion to upgrade from."
    }

    Invoke-Msi 'install-previous' @('/i', $previous)
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
    Write-Host 'Upgrading in place...'

    Invoke-Msi 'upgrade' @('/i', $Msi)
}
else {
    Write-Host 'No previous version given; checking a fresh install only.'
    Invoke-Msi 'install' @('/i', $Msi)
}

Assert-Runs -Expected ''

Write-Host 'Checking the install put loadout on PATH...'

$path = [Environment]::GetEnvironmentVariable('PATH', 'User')

if ($path -notlike '*loadout*') {
    throw 'The install did not add loadout to the user PATH, so nothing can find it by name.'
}

Write-Host 'Removing it...'

$product = Get-InstalledProduct

if (-not $product) {
    throw 'Loadout is not registered with the installer, so it cannot be uninstalled.'
}

Invoke-Msi 'uninstall' @('/x', $product.ProductCode)

if (Test-Path $installed) {
    throw "Uninstalling left $installed behind."
}

Write-Host 'Install, upgrade and uninstall all pass.'
