<#
.SYNOPSIS
    Fails if any dependency carries a licence this project cannot ship under.

.DESCRIPTION
    A comment saying "keep this on version 6" is not a guarantee, and this
    project has a concrete reason to want one: FluentAssertions was Apache-2.0
    up to version 7 and became a paid commercial licence at version 8. A routine
    dependency bump would swap an open-source test library for one that is not,
    silently, and nothing in the build would notice.

    So the licence of every restored package is read from its own .nuspec and
    checked against an allowlist. A package whose licence is a file rather than
    an SPDX expression is reported rather than assumed: it may well be fine, but
    a human has to say so.

.PARAMETER Solution
    Solution or project to inspect. Defaults to the repository's solution.

.PARAMETER Detailed
    List every package, not only the ones that fail.
#>
[CmdletBinding()]
param(
    [string] $Solution,
    [switch] $Detailed
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot

if (-not $Solution) {
    $Solution = Join-Path $repositoryRoot 'AgentWorkspace.slnx'
}

# SPDX identifiers this project can ship under. Permissive only: a copyleft
# dependency would impose terms on everyone who uses the launcher, which is a
# decision for a person rather than for a build script.
$allowed = @(
    'MIT',
    'Apache-2.0',
    'BSD-2-Clause',
    'BSD-3-Clause',
    'ISC',
    'MS-PL',
    '0BSD',
    'Unlicense'
)

# Packages that predate SPDX expressions in NuGet and declare a licence URL
# instead. Each was checked by hand and what was found is recorded here, so the
# next person does not have to repeat the work or take it on trust. Anything not
# on this list and not carrying an expression fails the run.
$reviewed = @{
    # dotnet/corefx LICENSE.TXT. Reached through FluentAssertions 6, not
    # referenced directly.
    'System.Configuration.ConfigurationManager' = 'MIT (dotnet/corefx)'
    'System.Security.Cryptography.ProtectedData' = 'MIT (dotnet/corefx)'

    # xunit/xunit license.txt.
    'xunit.abstractions' = 'Apache-2.0 (xunit)'
}

Write-Host 'Restoring so the package list is current...'
& dotnet restore $Solution --nologo --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }

$assets = Get-ChildItem $repositoryRoot -Filter 'project.assets.json' -Recurse -File |
    Where-Object { $_.FullName -like '*obj*' }

if (-not $assets) { throw 'No restored projects were found.' }

$packagesRoot = if ($env:NUGET_PACKAGES) {
    $env:NUGET_PACKAGES
} else {
    Join-Path $HOME '.nuget/packages'
}

$seen = @{}
$problems = @()

foreach ($file in $assets) {
    $document = Get-Content $file.FullName -Raw | ConvertFrom-Json

    foreach ($library in $document.libraries.PSObject.Properties) {
        if ($library.Value.type -ne 'package') { continue }
        if ($seen.ContainsKey($library.Name)) { continue }

        $seen[$library.Name] = $true

        $identifier, $version = $library.Name -split '/', 2
        $nuspec = Join-Path $packagesRoot "$($identifier.ToLowerInvariant())/$version/$($identifier.ToLowerInvariant()).nuspec"

        if (-not (Test-Path $nuspec)) {
            $problems += "$identifier $version - no .nuspec found; cannot determine the licence"
            continue
        }

        [xml] $manifest = Get-Content $nuspec -Raw

        # Strict mode makes a missing property an error rather than a null, and
        # plenty of older packages declare no licence element at all.
        $metadata = $manifest.package.metadata
        $license = if ($metadata.PSObject.Properties.Name -contains 'license') {
            $metadata.license
        } else {
            $null
        }

        $expression = if ($license -is [string]) {
            $license
        } elseif ($null -ne $license) {
            if ($license.type -eq 'expression') { $license.'#text' } else { $null }
        } else {
            $null
        }

        if ($expression -and ($allowed -contains $expression)) {
            if ($Detailed) { Write-Host "  ok    $identifier $version  $expression" }
            continue
        }

        if (-not $expression -and $reviewed.ContainsKey($identifier)) {
            if ($Detailed) { Write-Host "  ok    $identifier $version  $($reviewed[$identifier]) (reviewed)" }
            continue
        }

        $problems += if ($expression) {
            "$identifier $version - licence '$expression' is not on the allowlist"
        } else {
            "$identifier $version - licence is a file rather than an SPDX expression and has not been reviewed"
        }
    }
}

Write-Host ''
Write-Host "Checked $($seen.Count) package(s)."

if ($problems.Count -gt 0) {
    Write-Host ''
    Write-Host 'These dependencies cannot be shipped under this project''s licence:' -ForegroundColor Red

    foreach ($problem in $problems) {
        Write-Host "  $problem" -ForegroundColor Red
    }

    Write-Host ''
    Write-Host 'Either replace the dependency, or add it to $reviewed in this script with a note'
    Write-Host 'saying what its licence actually permits.'

    exit 1
}

Write-Host 'Every dependency is permissively licensed.' -ForegroundColor Green
