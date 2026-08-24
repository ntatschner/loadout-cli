<#
.SYNOPSIS
    Signs Windows binaries with Azure Trusted Signing.

.DESCRIPTION
    Public trust without a private key on the build machine. The certificate
    lives in Azure; signtool talks to it through Microsoft's signing dlib, and
    authentication is the OIDC token azure/login already exchanged. There is no
    PFX to store, leak or rotate.

    Signing is opt-in and driven entirely by environment. With
    ARTIFACT_SIGNING_ACCOUNT unset this writes a notice and exits zero, so an
    ordinary local build produces an unsigned binary rather than failing. That
    is the same switch the workflow gates on, which means a developer and CI
    take the same path through installer.ps1.

    Half-configured is treated as an error rather than a fallback: an account
    with no endpoint or profile means somebody set one secret and not the rest,
    and quietly shipping unsigned would hide it until a user saw the warning
    Windows shows for unknown publishers.

.PARAMETER Path
    Files to sign. Each is signed and then verified.

.PARAMETER Required
    Fail when signing is not configured, instead of skipping. Used by the
    release workflow, where an unsigned artefact must never be published
    silently.
#>
param(
    [Parameter(Mandatory)]
    [string[]] $Path,

    [switch] $Required
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$account  = $env:ARTIFACT_SIGNING_ACCOUNT
$endpoint = $env:ARTIFACT_SIGNING_ENDPOINT
$profile  = $env:ARTIFACT_SIGNING_PROFILE

if (-not $account) {
    if ($Required) {
        throw 'ARTIFACT_SIGNING_ACCOUNT is not set, and signing was required.'
    }

    Write-Host 'Artifact Signing is not configured; leaving these files unsigned.'
    return
}

foreach ($pair in @{ ARTIFACT_SIGNING_ENDPOINT = $endpoint; ARTIFACT_SIGNING_PROFILE = $profile }.GetEnumerator()) {
    if (-not $pair.Value) {
        throw "$($pair.Key) is empty but ARTIFACT_SIGNING_ACCOUNT is set — refusing to build a half-configured signing path."
    }
}

$files = foreach ($candidate in $Path) {
    if (-not (Test-Path $candidate)) { throw "There is nothing to sign at '$candidate'." }
    (Resolve-Path $candidate).Path
}

# Reused across calls within one job. installer.ps1 signs twice — the payload
# before packaging and the package after — and downloading the dlib each time
# would double a slow step for nothing.
$tools = Join-Path ($env:RUNNER_TEMP ?? $env:TEMP) 'loadout-signing'
New-Item -ItemType Directory -Force -Path $tools | Out-Null

$dlib = Get-ChildItem $tools -Recurse -Filter 'Azure.CodeSigning.Dlib.dll' -ErrorAction SilentlyContinue |
        Select-Object -First 1

if (-not $dlib) {
    Write-Host 'Fetching the Trusted Signing client...'

    & nuget install Microsoft.Trusted.Signing.Client -Version 1.0.53 -OutputDirectory $tools -Verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'nuget install of Microsoft.Trusted.Signing.Client failed.' }

    $dlib = Get-ChildItem $tools -Recurse -Filter 'Azure.CodeSigning.Dlib.dll' |
            Select-Object -First 1
}

if (-not $dlib) { throw 'Azure.CodeSigning.Dlib.dll was not found after installing the client.' }

$metadata = Join-Path $tools 'metadata.json'

# ExcludeCredentials is not tuning, it is the difference between signing and
# hanging. The dlib authenticates with DefaultAzureCredential, which tries each
# method in turn; ManagedIdentityCredential probes 169.254.169.254, and on a
# host that is not in Azure that address does not refuse the connection, it
# stalls. The symptom is "Submitting digest for signing..." followed by nothing
# at all, with no error and no timeout.
#
# This list mirrors Microsoft's own signing action, leaving Environment and
# AzureCli. azure/login populates the Azure CLI, so AzureCliCredential is the
# one that has to survive.
@{
    Endpoint               = $endpoint
    CodeSigningAccountName = $account
    CertificateProfileName = $profile
    ExcludeCredentials     = @(
        'ManagedIdentityCredential',
        'WorkloadIdentityCredential',
        'SharedTokenCacheCredential',
        'VisualStudioCredential',
        'VisualStudioCodeCredential',
        'AzurePowerShellCredential',
        'AzureDeveloperCliCredential',
        'InteractiveBrowserCredential'
    )
} | ConvertTo-Json | Set-Content -Path $metadata -Encoding utf8

$signtool = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Recurse -Filter 'signtool.exe' -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -like '*x64*' } |
            Select-Object -Last 1

if (-not $signtool) { throw 'signtool.exe was not found. Install the Windows SDK.' }

foreach ($file in $files) {
    Write-Host "Signing $(Split-Path $file -Leaf)..."

    # The timestamp is what keeps a signature valid after the certificate
    # expires. Without it every release stops verifying the day the
    # certificate does.
    & $signtool.FullName sign `
        /v `
        /fd SHA256 `
        /tr 'http://timestamp.acs.microsoft.com' `
        /td SHA256 `
        /dlib $dlib.FullName `
        /dmdf $metadata `
        $file

    if ($LASTEXITCODE -ne 0) { throw "signtool failed for '$file' with exit code $LASTEXITCODE." }

    # Verified rather than assumed. signtool has been known to report success
    # for a signature that does not chain, and a release is the wrong place to
    # discover that.
    & $signtool.FullName verify /pa /v $file
    if ($LASTEXITCODE -ne 0) { throw "The signature on '$file' did not verify." }
}

Write-Host "Signed and verified $($files.Count) file(s) with profile $profile."
