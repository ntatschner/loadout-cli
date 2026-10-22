<#
.SYNOPSIS
    Publishes loadout for one runtime identifier, exactly as it ships.

.DESCRIPTION
    The one definition of the shipping build. CI runs this once per runtime,
    tests what it produces, and a release packages those same bytes rather than
    compiling again; package.ps1 and installer.ps1 run it themselves only when
    they are not handed a build, which is the local path.

    Self-contained and single-file, so the download needs no .NET installed
    (spec section 7). The version comes from Directory.Build.props and is not
    overridden here: on a release commit that file is the version being
    released, and a release refuses a tag that disagrees with it.

    Writes a SHA256SUMS beside the build covering every file in it. Every file,
    not only the executable, because a native library ships beside it on some
    runtimes and is as much a part of what was tested.

.PARAMETER Runtime
    Runtime identifier, for example osx-arm64.

.PARAMETER OutputDirectory
    Where the build is written. Emptied first.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
    [string] $Runtime,

    [Parameter(Mandatory)]
    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot 'src/Loadout.Cli/Loadout.Cli.csproj'

if (Test-Path $OutputDirectory) { Remove-Item $OutputDirectory -Recurse -Force }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$OutputDirectory = (Resolve-Path $OutputDirectory).Path

Write-Host "Publishing $Runtime..."

& dotnet publish $project `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:RestoreLockedMode=true `
    --output $OutputDirectory `
    --nologo `
    --verbosity quiet

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $Runtime." }

# Debug symbols and the generated XML API documentation are useful to us and
# nothing but weight to somebody downloading a release. The XML in particular is
# a quarter of a megabyte of text describing internals a user will never call.
Get-ChildItem $OutputDirectory -Include '*.pdb', '*.xml' -Recurse -File | Remove-Item -Force

# Two spaces before the name is what sha256sum -c expects; one will not verify.
# Names are relative and forward-slashed so the file checks the same way on
# every platform that reads it.
$sums = Get-ChildItem $OutputDirectory -Recurse -File |
    Sort-Object FullName |
    ForEach-Object {
        $name = [System.IO.Path]::GetRelativePath($OutputDirectory, $_.FullName).Replace('\', '/')
        $hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $name"
    }

[System.IO.File]::WriteAllText((Join-Path $OutputDirectory 'SHA256SUMS'), (($sums -join "`n") + "`n"))

$sums | ForEach-Object { Write-Host "  $_" }
