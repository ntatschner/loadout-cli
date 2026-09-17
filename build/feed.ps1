<#
.SYNOPSIS
    Writes the release feed that 'loadout update' reads.

.DESCRIPTION
    The release publishes archives and a SHA256SUMS manifest; the updater wants
    one JSON document naming, per runtime identifier, where the archive is and
    what it hashes to. Nothing produced that document, so 'loadout update' had
    nowhere to look on any machine — the setting existed, the feed never did.

    Built from the manifest rather than by hashing the files again. The manifest
    is what the release publishes and what somebody verifying a download checks
    against, so a feed derived from it cannot disagree with the checksums people
    are told to use.

    Archives only. The updater replaces the executable from the download, so it
    wants the .zip or .tar.gz — the .msi, .deb and .rpm install through the
    platform's own machinery and are not something to unpack over a running
    binary.

.PARAMETER Version
    The version being released, without the leading v.

.PARAMETER Directory
    Where the artifacts and SHA256SUMS are.

.PARAMETER BaseUrl
    Where the artifacts will be downloadable from, without a trailing slash.

.PARAMETER Notes
    Optional URL or text shown before updating.

.PARAMETER Output
    Where to write the feed. Defaults to feed.json beside the artifacts.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$Directory,
    [Parameter(Mandatory)][string]$BaseUrl,
    [string]$Notes,
    [string]$Output
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Directory)) {
    throw "No such directory: $Directory"
}

$manifestPath = Join-Path $Directory 'SHA256SUMS'

if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw "No SHA256SUMS in $Directory. The feed is built from the manifest, not from the files."
}

# sha256sum's own format: the hash, two spaces, then the name — with a '*'
# before the name when the file was read in binary mode, which is how the
# packaging scripts write it on Windows.
$hashes = @{}

foreach ($line in Get-Content -LiteralPath $manifestPath) {
    if ($line -match '^\s*([0-9a-fA-F]{64})\s+\*?(.+?)\s*$') {
        $hashes[$Matches[2]] = $Matches[1].ToLowerInvariant()
    }
}

if ($hashes.Count -eq 0) {
    throw "SHA256SUMS in $Directory has no usable lines."
}

# Windows ships the zip; everything else the tarball. One entry per platform
# that actually built: a feed missing a platform simply has no update for it,
# which the updater treats as an ordinary state rather than a failure.
$platforms = [ordered]@{
    'win-x64'     = "loadout-$Version-win-x64.zip"
    'win-arm64'   = "loadout-$Version-win-arm64.zip"
    'linux-x64'   = "loadout-$Version-linux-x64.tar.gz"
    'linux-arm64' = "loadout-$Version-linux-arm64.tar.gz"
    'osx-x64'     = "loadout-$Version-osx-x64.tar.gz"
    'osx-arm64'   = "loadout-$Version-osx-arm64.tar.gz"
}

$artifacts = [ordered]@{}

foreach ($rid in $platforms.Keys) {
    $name = $platforms[$rid]
    $path = Join-Path $Directory $name

    if (-not (Test-Path -LiteralPath $path)) {
        Write-Host "no archive for $rid, leaving it out of the feed"
        continue
    }

    if (-not $hashes.ContainsKey($name)) {
        # Refused rather than published without one. The updater will not
        # install an artifact with no hash, so a feed entry missing one is a
        # platform that would fail at the last step instead of being absent.
        throw "$name is in $Directory but not in SHA256SUMS, so its hash cannot be stated."
    }

    $artifacts[$rid] = [ordered]@{
        url    = "$BaseUrl/$name"
        sha256 = $hashes[$name]
        size   = (Get-Item -LiteralPath $path).Length
    }
}

if ($artifacts.Count -eq 0) {
    throw "No archives found in $Directory for version $Version."
}

$feed = [ordered]@{
    schemaVersion = 1
    version       = $Version
    released      = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    artifacts     = $artifacts
}

if ($Notes) {
    $feed['notes'] = $Notes
}

if (-not $Output) {
    $Output = Join-Path $Directory 'feed.json'
}

$json = $feed | ConvertTo-Json -Depth 5

# Without a BOM, because this is read over HTTP by a JSON parser rather than
# opened in an editor.
[System.IO.File]::WriteAllText($Output, $json, [System.Text.UTF8Encoding]::new($false))

Write-Host "feed for $Version with $($artifacts.Count) platform(s): $Output"
