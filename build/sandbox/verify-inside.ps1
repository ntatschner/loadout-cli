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

    # Run under the staged PowerShell 7, not the 5.1 this bootstrap is in.
    #
    # Start-Process -PassThru with redirected output does not expose ExitCode
    # on 5.1: it comes back empty, so every bounded check in the verification
    # compares against nothing and the run fails saying so. Verified against
    # both hosts rather than assumed — 7 returns the code, 5.1 returns nothing,
    # and neither caching the handle nor a second no-argument wait changes it.
    $pwsh = Join-Path $Payload 'pwsh\pwsh.exe'

    if (-not (Test-Path -LiteralPath $pwsh)) {
        throw "PowerShell 7 was not staged into the payload; the verification cannot run on 5.1."
    }

    $arguments = @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass',
        '-File', (Join-Path $Payload 'verify-windows-install.ps1'),
        '-Msi', $current.FullName,

        # The verification turns the Restart Manager off itself now. It used to
        # be done here, which meant two places knew the condition and only one
        # of them was the thing CI runs.
        '-DisableRestartManager')

    if ($previous) {
        Write-Host "Upgrading over $($previous.Name)."
        $arguments += @('-PreviousMsi', $previous.FullName)
    }
    else {
        Write-Host 'No previous release was mapped in, so only a fresh install is checked.'
    }

    # Piped through this host rather than left to write to the console.
    # Start-Transcript records what PowerShell writes, not what a child process
    # does, so running it directly left the transcript with a hole exactly
    # where the interesting part goes — and a failure with no detail is the
    # thing this whole harness exists to stop producing.
    & $pwsh @arguments 2>&1 |
        Tee-Object -FilePath (Join-Path $Results 'verification.log') |
        ForEach-Object { Write-Host $_ }

    if ($LASTEXITCODE -ne 0) {
        throw "The verification exited $LASTEXITCODE."
    }

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

    # Closed from the inside, because nothing on the host can tell whether this
    # has finished or is still working. The verdict is already written to a
    # mapped folder, which is the host's own filesystem rather than anything
    # that needs flushing, so it survives the machine going away underneath it.
    #
    # A moment first: the transcript handle has just been released, and taking
    # the machine down in the same breath has no upside.
    Start-Sleep -Seconds 3

    & shutdown.exe /s /t 0
}
