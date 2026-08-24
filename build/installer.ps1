<#
.SYNOPSIS
    Builds a native installer for one runtime identifier.

.DESCRIPTION
    Produces an MSI on Windows targets and a .deb or .rpm on Linux ones
    (spec sections 18 to 20). The archives that build/package.ps1 produces stay
    the documented route for people who would rather not install anything; this
    is for the ones who would.

    Each format is built by the tooling that owns it — WiX for MSI, dpkg-deb
    for .deb, rpmbuild for .rpm — rather than by assembling the container
    formats by hand. A .deb written with an ar writer of our own would work
    until the day it did not, and would fail somewhere in the middle of
    somebody's package manager.

.PARAMETER Runtime
    Runtime identifier, for example linux-x64.

.PARAMETER Version
    Version string used in the package name and metadata.

.PARAMETER OutputDirectory
    Where the installers are written. Created if absent.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64')]
    [string] $Runtime,

    [string] $Version = '0.1.0',

    [string] $OutputDirectory = 'artifacts'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot 'src/Loadout.Cli/Loadout.Cli.csproj'
$staging = Join-Path $repositoryRoot "artifacts/installer/$Runtime"
$payload = Join-Path $staging 'payload'

$output = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory
} else {
    Join-Path $repositoryRoot $OutputDirectory
}

if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Force -Path $payload | Out-Null
New-Item -ItemType Directory -Force -Path $output | Out-Null

Write-Host "Publishing $Runtime..."

& dotnet publish $project `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `
    --output $payload `
    -p:Version=$Version `
    --nologo `
    --verbosity quiet

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $Runtime." }

# Same reasoning as the archive build: symbols and generated API documentation
# are weight to somebody installing this, and the XML alone is a quarter of a
# megabyte describing internals nobody outside this repository will call.
Get-ChildItem $payload -Include '*.pdb', '*.xml' -Recurse -File | Remove-Item -Force

# Signed here, before packaging, so the executable inside the package carries a
# signature too. Signing only the installer leaves the thing it installs
# unsigned, which is what SmartScreen actually looks at when somebody runs it.
#
# A no-op unless Artifact Signing is configured in the environment, so a local
# build takes this same path and simply produces an unsigned binary.
if ($Runtime -like 'win-*') {
    & (Join-Path $PSScriptRoot 'sign-windows.ps1') -Path (Join-Path $payload 'loadout.exe')
    if ($LASTEXITCODE -ne 0) { throw "Signing the payload failed for $Runtime." }
}

# The Debian architecture names differ from the .NET runtime identifiers, and
# a package built with the wrong one installs on nothing.
$debianArchitecture = @{ 'linux-x64' = 'amd64'; 'linux-arm64' = 'arm64' }
$rpmArchitecture = @{ 'linux-x64' = 'x86_64'; 'linux-arm64' = 'aarch64' }

function Publish-Result {
    param([string] $Path)

    $hash = (Get-FileHash $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    $name = Split-Path $Path -Leaf

    # Two spaces before the name is what sha256sum -c expects; one will not verify.
    "$hash  $name" | Set-Content -Path "$Path.sha256" -Encoding ascii -NoNewline

    Write-Host "  $name"
    Write-Host "  sha256 $hash"
}

if ($Runtime.StartsWith('win-')) {
    if (-not (Get-Command wix -ErrorAction SilentlyContinue)) {
        throw "The WiX toolset is not installed. Run: dotnet tool install --global wix"
    }

    $msi = Join-Path $output "loadout-$Version-$Runtime.msi"
    if (Test-Path $msi) { Remove-Item $msi -Force }

    # The MSI architecture must match the payload. An x64 package refuses to
    # install on arm64, which is better than installing and then not running.
    $platform = if ($Runtime -eq 'win-arm64') { 'arm64' } else { 'x64' }

    & wix build (Join-Path $PSScriptRoot 'windows/loadout.wxs') `
        -arch $platform `
        -define "Version=$Version" `
        -define "PublishDir=$payload" `
        -out $msi

    if ($LASTEXITCODE -ne 0) { throw "wix build failed for $Runtime." }

    # WiX writes debug symbols beside the package. Same reasoning as stripping
    # them from the payload: useful to us, dead weight next to a release, and
    # one careless upload glob away from shipping.
    Remove-Item ([System.IO.Path]::ChangeExtension($msi, '.wixpdb')) -ErrorAction SilentlyContinue

    # The package itself, after WiX has written it. This is the signature
    # Windows checks when the MSI is double-clicked; the one on the payload
    # above is what it checks afterwards, when the installed binary is run.
    & (Join-Path $PSScriptRoot 'sign-windows.ps1') -Path $msi
    if ($LASTEXITCODE -ne 0) { throw "Signing the installer failed for $Runtime." }

    Publish-Result $msi
    Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue

    return
}

if ($IsWindows) {
    # Refused rather than approximated. Building a Linux package on Windows
    # means writing the container format by hand and hoping, and a package that
    # is subtly malformed fails inside somebody else's package manager where
    # the error will make no sense to them.
    throw "Linux packages are built on Linux, where dpkg-deb and rpmbuild are. " +
          "The release workflow does this on its Ubuntu leg."
}

$architecture = $debianArchitecture[$Runtime]
$root = Join-Path $staging 'root'
$binDirectory = Join-Path $root 'usr/lib/loadout'
$linkDirectory = Join-Path $root 'usr/bin'

New-Item -ItemType Directory -Force -Path $binDirectory, $linkDirectory | Out-Null
Copy-Item (Join-Path $payload '*') $binDirectory -Recurse -Force

chmod 0755 (Join-Path $binDirectory 'loadout')
if ($LASTEXITCODE -ne 0) { throw 'chmod failed for the published binary.' }

# The binary lives under /usr/lib beside the runtime it needs, with a symlink on
# PATH. Putting a hundred-file self-contained publish directly into /usr/bin
# would be antisocial.
& ln -sf '/usr/lib/loadout/loadout' (Join-Path $linkDirectory 'loadout')
if ($LASTEXITCODE -ne 0) { throw 'Could not create the /usr/bin symlink.' }

if (Get-Command dpkg-deb -ErrorAction SilentlyContinue) {
    $debianDirectory = Join-Path $root 'DEBIAN'
    New-Item -ItemType Directory -Force -Path $debianDirectory | Out-Null

    # Self-contained, so the only dependency is a libc to run against. Naming
    # a .NET runtime here would be false and would block installation on a
    # machine that can run this perfectly well.
    $control = @"
Package: loadout
Version: $Version
Section: devel
Priority: optional
Architecture: $architecture
Maintainer: Loadout
Description: Loadout
 Launches AI coding agents against registered projects, keeping agent
 configuration and context in a central workspace repository rather than in
 the application repositories themselves.
"@

    # Debian control files are LF-terminated and must end with a newline.
    $control = $control.Replace("`r`n", "`n") + "`n"
    [System.IO.File]::WriteAllText((Join-Path $debianDirectory 'control'), $control)

    $deb = Join-Path $output "loadout_${Version}_$architecture.deb"
    if (Test-Path $deb) { Remove-Item $deb -Force }

    & dpkg-deb --build --root-owner-group $root $deb
    if ($LASTEXITCODE -ne 0) { throw "dpkg-deb failed for $Runtime." }

    Publish-Result $deb
}
else {
    Write-Host '  dpkg-deb not found; skipping the .deb.'
}

if (Get-Command rpmbuild -ErrorAction SilentlyContinue) {
    $rpmRoot = Join-Path $staging 'rpm'
    New-Item -ItemType Directory -Force -Path (Join-Path $rpmRoot 'SPECS') | Out-Null

    $rpmArch = $rpmArchitecture[$Runtime]

    # RPM forbids a hyphen in Version, so a pre-release tag like v0.2.0-beta.1
    # fails the build outright. The conventional mapping puts the pre-release
    # into Release with a leading 0., which also orders it correctly: rpm sorts
    # 0.beta.1 before 1, so the beta upgrades cleanly to the final release.
    if ($Version -match '^([^-]+)-(.+)$') {
        $rpmVersion = $Matches[1]
        $rpmRelease = '0.' + ($Matches[2] -replace '-', '.')
    }
    else {
        $rpmVersion = $Version
        $rpmRelease = '1'
    }

    $spec = @"
Name:           loadout
Version:        $rpmVersion
Release:        $rpmRelease
Summary:        Loadout
License:        MIT
AutoReqProv:    no

%description
Launches AI coding agents against registered projects, keeping agent
configuration and context in a central workspace repository rather than in the
application repositories themselves.

%install
mkdir -p %{buildroot}/usr/lib/loadout
mkdir -p %{buildroot}/usr/bin
cp -r $binDirectory/* %{buildroot}/usr/lib/loadout/
ln -sf /usr/lib/loadout/loadout %{buildroot}/usr/bin/loadout

%files
/usr/lib/loadout
/usr/bin/loadout
"@

    $specPath = Join-Path $rpmRoot 'SPECS/loadout.spec'
    [System.IO.File]::WriteAllText($specPath, $spec.Replace("`r`n", "`n") + "`n")

    # AutoReqProv is off above because rpmbuild would otherwise scan the
    # published .NET libraries and generate dependencies on shared objects that
    # ship inside this very package.
    # --target is what makes a cross-architecture build possible, and it is also
    # why the spec above carries no BuildArch. Setting both is what rpm 4.18
    # rejects with "No compatible architectures found for build": the spec
    # pins an architecture the host cannot build for, and --target pinning the
    # same one does not resolve the contradiction. --target alone does.
    #
    # Confirmed against rpm 4.18.2, the version on the runner: BuildArch plus
    # --target fails, --target alone writes the aarch64 package.
    & rpmbuild --define "_topdir $rpmRoot" --define "_binary_payload w2.xzdio" --target $rpmArch -bb $specPath
    if ($LASTEXITCODE -ne 0) { throw "rpmbuild failed for $Runtime." }

    $rpm = Get-ChildItem (Join-Path $rpmRoot 'RPMS') -Filter '*.rpm' -Recurse |
        Select-Object -First 1

    if ($null -eq $rpm) { throw 'rpmbuild reported success but produced no package.' }

    $destination = Join-Path $output $rpm.Name
    Move-Item $rpm.FullName $destination -Force

    Publish-Result $destination
}
else {
    Write-Host '  rpmbuild not found; skipping the .rpm.'
}

Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
