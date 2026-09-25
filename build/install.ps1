<#
.SYNOPSIS
    Installs loadout on Windows.

.DESCRIPTION
    irm https://github.com/ntatschner/loadout-cli/releases/latest/download/install.ps1 | iex

    Works out the architecture, downloads that release's MSI, checks it against
    the release's SHA256SUMS and its Authenticode signature, and installs it per
    user with no elevation. The MSI puts loadout on PATH; this also adds it to
    the PATH of the session it ran in, so loadout works without opening a new
    terminal.

    Written for Windows PowerShell 5.1 as well as PowerShell 7, because 5.1 is
    what a fresh Windows machine opens.

.PARAMETER Version
    Install that release rather than the latest.

.PARAMETER BaseUrl
    Fetch from a mirror of a release's assets: a URL, or a directory such as a
    file share.

.EXAMPLE
    & ([scriptblock]::Create((irm https://github.com/ntatschner/loadout-cli/releases/latest/download/install.ps1))) -Version 0.49.1

.EXAMPLE
    ./install.ps1 -WhatIf
    Downloads and verifies, installs nothing.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $Version,
    [string] $BaseUrl
)

# Everything is inside a function called on the last line: piped into iex, a
# download cut off halfway defines nothing and runs nothing. And nothing here
# calls exit, which under iex would close the terminal the person typed into;
# a refusal throws instead.
function Install-Loadout {
    [CmdletBinding(SupportsShouldProcess)]
    param([string] $Version, [string] $BaseUrl)

    $ErrorActionPreference = 'Stop'

    # Invoke-WebRequest's progress bar slows a 5.1 download by an order of
    # magnitude.
    $ProgressPreference = 'SilentlyContinue'

    $releases = 'https://github.com/ntatschner/loadout-cli/releases'

    if ($PSVersionTable.PSEdition -eq 'Core' -and -not $IsWindows) {
        throw 'install.ps1 is the Windows installer. On Linux or macOS, use install.sh from the same release.'
    }

    # 5.1 on an older build still offers TLS 1.0 first, which GitHub refuses.
    [Net.ServicePointManager]::SecurityProtocol =
        [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

    $architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
    switch ($architecture.ToString()) {
        'X64' { $rid = 'win-x64' }
        'Arm64' { $rid = 'win-arm64' }
        default { throw "No Windows build is published for $architecture." }
    }

    $Version = $Version.TrimStart('v')

    if (-not $BaseUrl) {
        $BaseUrl = if ($Version) { "$releases/download/v$Version" } else { "$releases/latest/download" }
    }
    $BaseUrl = $BaseUrl.TrimEnd('/', '\')

    $temporary = Join-Path ([IO.Path]::GetTempPath()) ('loadout-install-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $temporary -WhatIf:$false | Out-Null

    # A plain copy when the source is a directory: a mirror on a file share is
    # a mirror too, and Invoke-WebRequest on 7 cannot read file:// at all.
    function Get-Asset([string] $Name) {
        $destination = Join-Path $temporary $Name

        if ($BaseUrl -match '^https?://') {
            try {
                Invoke-WebRequest -Uri "$BaseUrl/$Name" -OutFile $destination -UseBasicParsing
            }
            catch {
                throw "Couldn't download $BaseUrl/$Name. $($_.Exception.Message)"
            }
        }
        else {
            $source = Join-Path $BaseUrl $Name
            if (-not (Test-Path -LiteralPath $source)) { throw "There is no $Name in $BaseUrl." }
            Copy-Item -LiteralPath $source -Destination $destination -WhatIf:$false
        }

        $destination
    }

    try {
        if (-not $Version) {
            $feed = Get-Content -LiteralPath (Get-Asset 'feed.json') -Raw | ConvertFrom-Json
            $Version = $feed.version
            if (-not $Version) { throw "$BaseUrl/feed.json doesn't name a version." }
        }

        $name = "loadout-$Version-$rid.msi"

        Write-Host "Downloading loadout $Version for $rid..."
        $msi = Get-Asset $name
        $manifest = Get-Asset 'SHA256SUMS'

        # Matched on the name, in sha256sum's own format with or without the
        # binary-mode star, so a manifest for other files is not believed.
        $expected = $null
        foreach ($line in Get-Content -LiteralPath $manifest) {
            $fields = $line.Trim() -split '\s+', 2
            if ($fields.Count -eq 2 -and $fields[1].TrimStart('*') -eq $name) {
                $expected = $fields[0]
                break
            }
        }
        if (-not $expected) { throw "SHA256SUMS doesn't list $name. Not installing." }

        # Hashed with .NET rather than Get-FileHash, which on 5.1 obeys -WhatIf,
        # returns nothing, and offers no way to be told otherwise.
        $stream = [IO.File]::OpenRead($msi)
        $sha = [Security.Cryptography.SHA256]::Create()
        try {
            $actual = -join ($sha.ComputeHash($stream) | ForEach-Object { $_.ToString('x2') })
        }
        finally {
            $sha.Dispose()
            $stream.Dispose()
        }
        if ($actual -ne $expected) { throw "Checksum mismatch for $name. Not installing." }
        Write-Host 'Checksum verified.'

        # The checksum comes from the same place as the file, so it proves the
        # download is intact, not who made it. The signature is what says that.
        $signature = Get-AuthenticodeSignature -LiteralPath $msi
        if ($signature.Status -ne 'Valid') {
            throw "$name isn't validly signed ($($signature.Status)). Not installing."
        }
        Write-Host "Signature verified: $($signature.SignerCertificate.Subject)"

        if (-not $PSCmdlet.ShouldProcess($name, 'Install per user with msiexec')) {
            return
        }

        # No output redirection, deliberately: Windows PowerShell 5.1 reports an
        # empty ExitCode from Start-Process -PassThru when output is redirected.
        $run = Start-Process -FilePath 'msiexec.exe' `
            -ArgumentList @('/i', "`"$msi`"", '/passive', '/norestart') `
            -Wait -PassThru

        switch ($run.ExitCode) {
            0 { }
            3010 { Write-Host 'Installed. Windows asks for a restart to finish.' }
            1602 { throw 'The install was cancelled.' }
            1603 { throw 'msiexec failed (1603). If loadout is running, close it and try again: an upgrade cannot replace a launcher that is in use.' }
            default { throw "msiexec failed with exit code $($run.ExitCode)." }
        }

        # The MSI wrote PATH for new terminals. This one read PATH when it
        # started, so it gets the user entries added to what it already has.
        $known = $env:Path -split ';'
        $added = [Environment]::GetEnvironmentVariable('Path', 'User') -split ';' |
            Where-Object { $_ -and $known -notcontains $_ }
        if ($added) { $env:Path = ($known + $added) -join ';' }

        Write-Host ''
        Write-Host "Installed loadout $Version."
        Write-Host 'Next: loadout setup'
    }
    finally {
        Remove-Item -LiteralPath $temporary -Recurse -Force -ErrorAction SilentlyContinue -WhatIf:$false
    }
}

Install-Loadout @PSBoundParameters
