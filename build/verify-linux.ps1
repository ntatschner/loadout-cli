<#
.SYNOPSIS
    Runs the Linux build, tests and packaging in a container.

.DESCRIPTION
    Everything below the platform seam is untestable from the host it was not
    written for, and "it compiles" is not the same claim as "it works". This
    gives a developer on Windows or macOS a way to run the Linux half before
    pushing, rather than discovering it in CI or, worse, not at all.

    The container is a development tool only. Spec section 1 forbids a container
    from being any part of how the launcher runs, and CI still runs these tests
    natively on its Ubuntu leg.

.PARAMETER Version
    Version string used for the archive and packages built inside the container.

.PARAMETER Architecture
    Which architecture to verify. arm64 runs under emulation, which is slow but
    is the only way to execute a linux-arm64 build without an arm64 machine:
    that build is otherwise cross-compiled and never run anywhere.

.PARAMETER Rebuild
    Rebuild the image from scratch, ignoring the layer cache.
#>
[CmdletBinding()]
param(
    [string] $Version = '0.1.0',

    [ValidateSet('amd64', 'arm64')]
    [string] $Architecture = 'amd64',

    [switch] $Rebuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$image = "loadout-linux-verify:$Architecture"
$platform = "linux/$Architecture"

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw 'Docker is not available. This script exists only to run the Linux checks on a non-Linux host.'
}

$buildArguments = @(
    'build',
    '--platform', $platform,
    '--tag', $image,
    '--file', 'build/docker/Dockerfile',
    '.'
)

if ($Rebuild) { $buildArguments = @('build', '--no-cache') + $buildArguments[1..($buildArguments.Length - 1)] }

Push-Location $repositoryRoot
try {
    & docker @buildArguments
    if ($LASTEXITCODE -ne 0) { throw 'The verification image could not be built.' }

    if ($Architecture -ne 'amd64') {
        Write-Host "Running under $platform emulation. This is considerably slower than native."
    }

    & docker run --rm --platform $platform --env "LOADOUT_VERSION=$Version" $image
    if ($LASTEXITCODE -ne 0) { throw "The Linux verification failed for $platform." }
}
finally {
    Pop-Location
}
