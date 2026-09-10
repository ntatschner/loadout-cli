<#
.SYNOPSIS
    Runs the install verification inside a Windows Sandbox and writes the
    result out to the host.

.DESCRIPTION
    The guest half of build/verify-windows-sandbox.ps1. Everything it needs is
    already mapped in, so it reaches the network for nothing: the two packages
    and the verification script are handed to it, and the only thing it sends
    back is a transcript and one line saying what happened.

    It sets the Restart Manager policy itself, and that is the point of running
    this at all. The failure the verification exists to catch — an upgrade over
    a running launcher ending in 1603 — only happens where the Restart Manager
    is disabled, which is a policy on managed Windows builds and is not the
    default anywhere. A CI runner has it enabled. So does a fresh sandbox. Both
    would install cleanly over a running launcher and report success for a
    package that fails on the machines the defect was reported from.

    Setting it here turns the precondition into something the test states
    rather than something the machine happened to have.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Payload,

    [Parameter(Mandatory)]
    [string] $Results
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

New-Item -ItemType Directory -Force -Path $Results | Out-Null

$transcript = Join-Path $Results 'transcript.log'
$verdict = Join-Path $Results 'verdict.txt'

Start-Transcript -Path $transcript -Force | Out-Null

try {
    Write-Host '== Disabling the Restart Manager, which is the condition under test =='

    # The same policy the reported failures were on. Written to the machine
    # hive, which is where the installer reads it.
    $key = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Installer'

    New-Item -Path $key -Force | Out-Null
    New-ItemProperty -Path $key -Name 'DisableAutomaticApplicationShutdown' `
        -Value 1 -PropertyType DWord -Force | Out-Null

    $set = (Get-ItemProperty -Path $key).DisableAutomaticApplicationShutdown

    if ($set -ne 1) {
        throw "The Restart Manager policy did not take: it reads '$set'."
    }

    Write-Host 'DisableAutomaticApplicationShutdown = 1'

    $current = Get-ChildItem -Path $Payload -Filter '*win-x64.msi' |
        Where-Object { $_.Name -notlike '*previous*' } |
        Sort-Object Name -Descending |
        Select-Object -First 1

    $previous = Get-ChildItem -Path $Payload -Filter 'previous-*win-x64.msi' |
        Select-Object -First 1

    if (-not $current) {
        throw "No package to install was mapped in. $Payload holds: " +
            ((Get-ChildItem $Payload | Select-Object -ExpandProperty Name) -join ', ')
    }

    Write-Host "== Verifying $($current.Name) =="

    $arguments = @{
        Msi = $current.FullName
    }

    if ($previous) {
        Write-Host "Upgrading over $($previous.Name)."
        $arguments['PreviousMsi'] = $previous.FullName
    }
    else {
        Write-Host 'No previous release was mapped in, so only a fresh install is checked.'
    }

    & (Join-Path $Payload 'verify-windows-install.ps1') @arguments

    'PASS' | Set-Content -Path $verdict -Encoding ascii
    Write-Host '== PASS =='
}
catch {
    # The message as well as the word, because the sandbox closes and takes
    # every other trace of this with it.
    "FAIL $($_.Exception.Message)" | Set-Content -Path $verdict -Encoding ascii

    Write-Host "== FAIL: $($_.Exception.Message) =="
}
finally {
    Stop-Transcript | Out-Null
}
