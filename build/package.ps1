<#
.SYNOPSIS
    Publishes loadout for one runtime identifier and packages it for release.

.DESCRIPTION
    Written in PowerShell because pwsh runs on all three Tier-1 platforms, so
    the packaging step is the same command on every CI leg rather than three
    scripts that drift apart.

    Produces a zip on Windows targets and a tar.gz elsewhere, matching what
    people expect to download for their platform (spec sections 18 to 20), plus
    a SHA-256 file so a download can be verified.

.PARAMETER Runtime
    Runtime identifier, for example osx-arm64.

.PARAMETER Version
    Version string used in the archive name.

.PARAMETER OutputDirectory
    Where the archives are written. Created if absent.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
    [string] $Runtime,

    [string] $Version = '0.1.0',

    [string] $OutputDirectory = 'artifacts'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot 'src/Loadout.Cli/Loadout.Cli.csproj'
$staging = Join-Path $repositoryRoot "artifacts/staging/$Runtime"
$output = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory
} else {
    Join-Path $repositoryRoot $OutputDirectory
}

if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Force -Path $staging | Out-Null
New-Item -ItemType Directory -Force -Path $output | Out-Null

Write-Host "Publishing $Runtime..."

# Self-contained so the download needs no .NET installed, which is the whole
# point of shipping a single binary (spec section 7).
& dotnet publish $project `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `

    -p:PublishSingleFile=true `
    --output $staging `
    -p:Version=$Version `
    -p:RestoreLockedMode=true `
    --nologo `
    --verbosity quiet

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $Runtime." }

# Debug symbols and the generated XML API documentation are useful to us and
# nothing but weight to somebody downloading a release. The XML in particular is
# a quarter of a megabyte of text describing internals a user will never call.
Get-ChildItem $staging -Include '*.pdb', '*.xml' -Recurse -File | Remove-Item -Force

# The archive ships the executable directly, with no installer around it, so
# this is the only signature a user of the .tar.gz or .zip ever sees. A no-op
# unless Artifact Signing is configured, which it is not on a developer
# machine.
if ($Runtime -like 'win-*') {
    & (Join-Path $PSScriptRoot 'sign-windows.ps1') -Path (Join-Path $staging 'loadout.exe')
    if ($LASTEXITCODE -ne 0) { throw "Signing the archive payload failed for $Runtime." }
}

Copy-Item (Join-Path $repositoryRoot 'README.md') $staging -Force

$license = Join-Path $repositoryRoot 'LICENSE'
if (Test-Path $license) { Copy-Item $license $staging -Force }

$stem = "loadout-$Version-$Runtime"

if ($Runtime.StartsWith('win-')) {
    $archive = Join-Path $output "$stem.zip"
    if (Test-Path $archive) { Remove-Item $archive -Force }

    Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $archive
}
else {
    $archive = Join-Path $output "$stem.tar.gz"
    if (Test-Path $archive) { Remove-Item $archive -Force }

    # The executable bit has to survive into the archive, or whoever extracts it
    # gets a binary they cannot run and no clue why. A Windows filesystem holds
    # no Unix mode to preserve, so the two hosts need different handling.
    $tarArguments = @()

    if ($IsWindows) {
        # GNU tar can stamp a mode onto every entry. The README becoming
        # executable is cosmetically odd and entirely harmless; a non-executable
        # loadout is neither. The tar shipped in Windows itself is bsdtar,
        # which rejects the option, so this needs the GNU tar from Git.
        $tarArguments += '--mode=0755'
    }
    else {
        # On a real Unix host the mode is genuine, so it is set precisely and
        # the README keeps its ordinary permissions.
        chmod 0755 (Join-Path $staging 'loadout')
        if ($LASTEXITCODE -ne 0) { throw "chmod failed for $Runtime." }
    }

    # The archive name is passed relative to the output directory rather than as
    # an absolute path: GNU tar reads a colon in the -f argument as a remote
    # host, so a Windows path beginning with a drive letter sends it looking for
    # a machine of that name. Only -f is affected, so -C keeps its full path.
    Push-Location $output
    try {
        & tar @tarArguments -czf "$stem.tar.gz" -C $staging .

        if ($LASTEXITCODE -ne 0) {
            throw "tar failed for $Runtime. On Windows this needs the GNU tar that ships with " +
                  "Git; the bsdtar built into Windows cannot set the executable bit."
        }
    }
    finally {
        Pop-Location
    }
}

$hash = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
$name = Split-Path $archive -Leaf

# Two spaces before the name is what sha256sum -c expects; one will not verify.
"$hash  $name" | Set-Content -Path "$archive.sha256" -Encoding ascii -NoNewline

# The staging tree is intermediate output and has no business sitting beside the
# release archives, where it would end up attached to a release by a careless
# upload glob.
Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "  $name"
Write-Host "  sha256 $hash"
