<#
.SYNOPSIS
    Installs the built MSI in a Windows Sandbox, upgrades over the previous
    release and removes it, without touching this machine.

.DESCRIPTION
    The same verification the release workflow runs, on a disposable machine
    you have locally rather than one you have to push a tag to reach.

    build/verify-windows-install.ps1 installs software and says so: it is meant
    for a CI runner and not for a machine somebody is using. That is the whole
    reason it was only ever run after a tag was pushed, which is a slow place
    to discover that an installer is wrong.

    A sandbox rather than a container, because the package is a per-user one.
    It installs to %LOCALAPPDATA%\Programs\loadout, adds a user PATH entry and
    writes a Start Menu shortcut, and a Windows container has no logged-in user
    to have any of those. The sandbox has a real profile and a real Start Menu,
    which is what the installer is aimed at.

    Nothing inside reaches the network. Both packages are staged here, where
    the credentials already are, and mapped in read-only.

.PARAMETER Msi
    The package to verify. Defaults to the newest win-x64 MSI in artifacts/.

.PARAMETER PreviousVersion
    A released version to install first, so the upgrade over a running
    launcher is exercised. Defaults to the latest release before this one.
    Pass 'none' to check only a fresh install.

.PARAMETER TimeoutMinutes
    How long to wait for the sandbox to report back.
#>
[CmdletBinding()]
param(
    [string] $Msi,

    [string] $PreviousVersion,

    [int] $TimeoutMinutes = 20,

    # Pinned rather than "latest", so a run today and a run next month are the
    # same run. Bump it deliberately.
    [string] $PwshVersion = '7.6.6'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot

function Fail([string] $message) {
    Write-Host $message
    exit 1
}

# Reported rather than enabled. Turning on a Windows feature needs elevation
# and reboots this machine's container stack; that is a decision for whoever
# owns the machine, and a script that silently takes it is a script nobody
# should run.
$sandbox = Join-Path $env:SystemRoot 'System32\WindowsSandbox.exe'

if (-not (Test-Path -LiteralPath $sandbox)) {
    Fail @"
Windows Sandbox is not installed.

It ships with Windows 11 Pro and needs enabling once, from an elevated prompt:

  Enable-WindowsOptionalFeature -Online -FeatureName 'Containers-DisposableClientVM' -All

That asks for a restart. Nothing else here needs elevation.
"@
}

if (-not $Msi) {
    $Msi = Get-ChildItem -Path (Join-Path $root 'artifacts') -Filter '*win-x64.msi' `
        -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}

if (-not $Msi -or -not (Test-Path -LiteralPath $Msi)) {
    Fail @"
No package to verify.

Build one first:
  pwsh ./build/installer.ps1 -Runtime win-x64 -Version <version>
"@
}

$Msi = (Resolve-Path -LiteralPath $Msi).Path

Write-Host "Verifying $(Split-Path -Leaf $Msi)"

# One directory per run, named so two runs cannot read each other's verdict.
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$work = Join-Path ([System.IO.Path]::GetTempPath()) "loadout-sandbox-$stamp"
$payload = Join-Path $work 'payload'
$results = Join-Path $work 'results'

New-Item -ItemType Directory -Force -Path $payload, $results | Out-Null

Copy-Item -LiteralPath $Msi -Destination $payload
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'verify-windows-install.ps1') -Destination $payload
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'sandbox\verify-inside.ps1') -Destination $payload

# PowerShell 7, staged, because a clean Windows image has only Windows
# PowerShell 5.1 and the verification cannot run there.
#
# Start-Process -PassThru with redirected output does not expose ExitCode on
# 5.1 — it comes back empty, while the same call on 7 returns the code — so
# every bounded check in the verification would compare against nothing. That
# is worse than not running it: the run reports a failure it cannot explain,
# and would report a pass just as readily.
#
# Staging the same host the release workflow uses also means the local run is
# the run CI does, rather than an approximation of it that can disagree.
$pwshCache = Join-Path ([System.IO.Path]::GetTempPath()) "loadout-sandbox-pwsh-$PwshVersion"
$pwshExe = Join-Path $pwshCache 'pwsh.exe'

if (-not (Test-Path -LiteralPath $pwshExe)) {
    Write-Host "Staging PowerShell $PwshVersion for the sandbox (once; it is cached after this)..."

    $zip = Join-Path ([System.IO.Path]::GetTempPath()) "PowerShell-$PwshVersion-win-x64.zip"

    if (-not (Test-Path -LiteralPath $zip)) {
        $url = "https://github.com/PowerShell/PowerShell/releases/download/v$PwshVersion/PowerShell-$PwshVersion-win-x64.zip"

        try {
            Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing
        }
        catch {
            Fail "PowerShell $PwshVersion could not be fetched from $url : $($_.Exception.Message)"
        }
    }

    New-Item -ItemType Directory -Force -Path $pwshCache | Out-Null
    Expand-Archive -LiteralPath $zip -DestinationPath $pwshCache -Force

    if (-not (Test-Path -LiteralPath $pwshExe)) {
        Fail "PowerShell $PwshVersion unpacked without a pwsh.exe in it."
    }
}

Copy-Item -LiteralPath $pwshCache -Destination (Join-Path $payload 'pwsh') -Recurse

if ($PreviousVersion -ne 'none') {
    if (-not $PreviousVersion) {
        Write-Host 'Finding the release before this one...'

        $tags = & gh release list --repo ntatschner/loadout-cli --limit 5 --json tagName 2>$null

        if ($LASTEXITCODE -eq 0 -and $tags) {
            $current = if ((Split-Path -Leaf $Msi) -match '(\d+\.\d+\.\d+)') { $Matches[1] } else { '' }

            $PreviousVersion = ($tags | ConvertFrom-Json |
                ForEach-Object { $_.tagName -replace '^v', '' } |
                Where-Object { $_ -ne $current } |
                Select-Object -First 1)
        }
    }

    if ($PreviousVersion) {
        Write-Host "Staging v$PreviousVersion to upgrade from..."

        $previous = Join-Path $payload "previous-$PreviousVersion-win-x64.msi"

        & gh release download "v$PreviousVersion" `
            --repo ntatschner/loadout-cli `
            --pattern '*win-x64.msi' `
            --output $previous `
            --clobber 2>&1 | Out-Null

        if ($LASTEXITCODE -ne 0 -or -not (Test-Path $previous)) {
            Write-Host "Could not stage v$PreviousVersion; checking a fresh install only."
            Remove-Item -LiteralPath $previous -ErrorAction SilentlyContinue
        }
    }
    else {
        Write-Host 'No earlier release found; checking a fresh install only.'
    }
}

# Mapped folders arrive on the sandbox user's desktop under their own name.
$inside = 'C:\Users\WDAGUtilityAccount\Desktop'

$wsb = Join-Path $work 'verify.wsb'

@"
<Configuration>
  <MappedFolders>
    <MappedFolder>
      <HostFolder>$payload</HostFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
    <MappedFolder>
      <HostFolder>$results</HostFolder>
      <ReadOnly>false</ReadOnly>
    </MappedFolder>
  </MappedFolders>
  <LogonCommand>
    <Command>powershell.exe -ExecutionPolicy Bypass -NoProfile -File "$inside\payload\verify-inside.ps1" -Payload "$inside\payload" -Results "$inside\results"</Command>
  </LogonCommand>
  <Networking>Disable</Networking>
  <MemoryInMB>4096</MemoryInMB>
</Configuration>
"@ | Set-Content -Path $wsb -Encoding utf8

Write-Host "Starting the sandbox. It closes itself when it is done."

& $sandbox $wsb

$verdict = Join-Path $results 'verdict.txt'
$deadline = [DateTime]::UtcNow.AddMinutes($TimeoutMinutes)

while (-not (Test-Path -LiteralPath $verdict)) {
    if ([DateTime]::UtcNow -gt $deadline) {
        Fail @"
The sandbox did not report back within $TimeoutMinutes minute(s).

It may still be open. Anything it wrote is in:
  $results
"@
    }

    Start-Sleep -Seconds 5
}

$transcript = Join-Path $results 'transcript.log'

if (Test-Path -LiteralPath $transcript) {
    Get-Content -LiteralPath $transcript | Write-Host
}

$answer = (Get-Content -LiteralPath $verdict -Raw).Trim()

Write-Host ''

if ($answer -eq 'PASS') {
    Write-Host "The package installs, upgrades and uninstalls with the Restart Manager disabled."
    exit 0
}

Fail $answer
