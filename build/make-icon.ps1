<#
.SYNOPSIS
    Builds the application icon in every size and format the packages need.

.DESCRIPTION
    One master image in, an .ico and a set of .png files out. Run it after
    changing build/icon/loadout-1024.png and commit what it produces.

    The two smallest sizes are drawn rather than resampled, and that is the
    whole point of this script existing. Shrinking the master to 16px turned
    the outlined bays to mush and lost the chevron entirely, leaving a cyan
    smudge; at that size the arrangement is the only thing that can survive, so
    it is drawn as four solid squares with no chevron at all. At 32px the
    chevron reads, but only if it is drawn as geometry instead of being
    reduced.

    Every size was checked by eye at its real pixels before this was settled.

.PARAMETER Master
    The 1024px source. Must already have real transparency: a JPEG exported
    with a checkerboard behind it will be packaged with the checkerboard.
#>
[CmdletBinding()]
param(
    [string] $Master = "$PSScriptRoot/icon/loadout-1024.png",
    [string] $OutputDirectory = "$PSScriptRoot/icon"
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Add-Type -AssemblyName System.Drawing

if (-not (Test-Path $Master)) {
    throw "No master image at '$Master'."
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$tile  = [System.Drawing.Color]::FromArgb(255, 20, 30, 40)
$steel = [System.Drawing.Color]::FromArgb(255, 91, 110, 130)
$cyan  = [System.Drawing.Color]::FromArgb(255, 47, 211, 200)

function New-Canvas {
    param([int] $Size)

    $bitmap = New-Object System.Drawing.Bitmap $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)

    return @($bitmap, $graphics)
}

function Add-RoundedRect {
    param($Path, [single] $X, [single] $Y, [single] $W, [single] $H, [single] $R)

    $d = $R * 2

    if ($d -le 0) {
        $Path.AddRectangle((New-Object System.Drawing.RectangleF $X, $Y, $W, $H))
        return
    }

    $Path.AddArc($X, $Y, $d, $d, 180, 90)
    $Path.AddArc($X + $W - $d, $Y, $d, $d, 270, 90)
    $Path.AddArc($X + $W - $d, $Y + $H - $d, $d, $d, 0, 90)
    $Path.AddArc($X, $Y + $H - $d, $d, $d, 90, 90)
    $Path.CloseFigure()
}

function Fill-Rounded {
    param($Graphics, $Colour, [single] $X, [single] $Y, [single] $W, [single] $H, [single] $R)

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    Add-RoundedRect $path $X $Y $W $H $R
    $brush = New-Object System.Drawing.SolidBrush $Colour
    $Graphics.FillPath($brush, $path)
    $brush.Dispose(); $path.Dispose()
}

<#
.SYNOPSIS
    The 16px frame, drawn rather than reduced.
.DESCRIPTION
    No outlines and no chevron. A one pixel frame is mush at this size and a
    chevron inside a five pixel bay is three grey pixels doing nothing. What
    survives is the arrangement: four bays, one of them lit.
#>
function New-Sixteen {
    $canvas = New-Canvas 16
    $bitmap = $canvas[0]; $graphics = $canvas[1]

    Fill-Rounded $graphics $tile 0 0 16 16 3

    foreach ($slot in @(@(2, 2), @(9, 2), @(2, 9))) {
        Fill-Rounded $graphics $steel $slot[0] $slot[1] 5 5 1
    }

    Fill-Rounded $graphics $cyan 9 9 5 5 1

    $graphics.Dispose()
    return $bitmap
}

<#
.SYNOPSIS
    The 32px frame, drawn rather than reduced.
.DESCRIPTION
    Twelve pixel bays take a two pixel outline, and the chevron reads when it is
    drawn as geometry. The amber notch is dropped: at this size it is one
    indistinct pixel.
#>
function New-ThirtyTwo {
    $canvas = New-Canvas 32
    $bitmap = $canvas[0]; $graphics = $canvas[1]

    Fill-Rounded $graphics $tile 0 0 32 32 6

    $pen = New-Object System.Drawing.Pen $steel, 2

    foreach ($slot in @(@(3, 3), @(17, 3), @(3, 17))) {
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        Add-RoundedRect $path ($slot[0] + 1) ($slot[1] + 1) 10 10 2
        $graphics.DrawPath($pen, $path)
        $path.Dispose()
    }

    $pen.Dispose()

    Fill-Rounded $graphics $cyan 17 17 12 12 2

    $chevron = New-Object System.Drawing.Drawing2D.GraphicsPath
    $points = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF 21.0, 20.0),
        (New-Object System.Drawing.PointF 25.5, 23.0),
        (New-Object System.Drawing.PointF 21.0, 26.0))
    $chevron.AddLines($points)

    $pen = New-Object System.Drawing.Pen $tile, 2.6
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $graphics.DrawPath($pen, $chevron)

    $pen.Dispose(); $chevron.Dispose(); $graphics.Dispose()
    return $bitmap
}

function New-Resampled {
    param($Source, [int] $Size)

    $canvas = New-Canvas $Size
    $bitmap = $canvas[0]; $graphics = $canvas[1]
    $graphics.DrawImage($Source, (New-Object System.Drawing.Rectangle 0, 0, $Size, $Size))
    $graphics.Dispose()

    return $bitmap
}

function Get-PngBytes {
    param($Bitmap)

    $stream = New-Object System.IO.MemoryStream
    $Bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)

    return ,$stream.ToArray()
}

<#
.SYNOPSIS
    One icon frame as a device-independent bitmap.
.DESCRIPTION
    PNG frames are understood from Windows Vista onwards, but the small sizes
    are written this way because an installer's shortcut and Add/Remove Programs
    entry are read by older shell code than the desktop is.
#>
function Get-DibBytes {
    param($Bitmap)

    $width = $Bitmap.Width; $height = $Bitmap.Height
    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter $stream

    $writer.Write([int]40)
    $writer.Write([int]$width)
    $writer.Write([int]($height * 2))
    $writer.Write([int16]1)
    $writer.Write([int16]32)
    $writer.Write([int]0)
    $writer.Write([int]($width * $height * 4))
    0..3 | ForEach-Object { $writer.Write([int]0) }

    for ($y = $height - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $width; $x++) {
            $pixel = $Bitmap.GetPixel($x, $y)
            $writer.Write([byte]$pixel.B); $writer.Write([byte]$pixel.G)
            $writer.Write([byte]$pixel.R); $writer.Write([byte]$pixel.A)
        }
    }

    # Required by the format and unused, because the alpha channel carries
    # transparency. Rows are padded to four bytes.
    $maskRow = [int][Math]::Floor((($width + 31) / 32)) * 4

    for ($y = 0; $y -lt $height; $y++) {
        $writer.Write((New-Object byte[] $maskRow))
    }

    $writer.Flush()
    return ,$stream.ToArray()
}

$source = New-Object System.Drawing.Bitmap $Master

# A plain hashtable with the order held separately. An ordered dictionary reads
# an integer index as a position rather than a key, so $frames[256] asks for the
# two hundred and fifty-seventh entry and throws.
$sizes = @(256, 128, 64, 48, 32, 16)
$frames = @{}

foreach ($size in $sizes) {
    $frames[$size] = switch ($size) {
        32 { New-ThirtyTwo }
        16 { New-Sixteen }
        default { New-Resampled $source $size }
    }
}

foreach ($size in $sizes) {
    $frames[$size].Save(
        (Join-Path $OutputDirectory "loadout-$size.png"),
        [System.Drawing.Imaging.ImageFormat]::Png)
}

$entries = foreach ($size in $sizes) {
    if ($size -ge 128) { $bytes = Get-PngBytes $frames[$size] } else { $bytes = Get-DibBytes $frames[$size] }

    [pscustomobject]@{ Size = $size; Bytes = [byte[]]$bytes }
}

$icoPath = Join-Path $OutputDirectory 'loadout.ico'
$file = [System.IO.File]::Create($icoPath)
$writer = New-Object System.IO.BinaryWriter $file

$writer.Write([int16]0)
$writer.Write([int16]1)
$writer.Write([int16]$entries.Count)

$offset = 6 + (16 * $entries.Count)

foreach ($entry in $entries) {
    $dimension = if ($entry.Size -ge 256) { 0 } else { $entry.Size }

    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
    $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([int16]1); $writer.Write([int16]32)
    $writer.Write([int]$entry.Bytes.Length)
    $writer.Write([int]$offset)

    $offset += $entry.Bytes.Length
}

foreach ($entry in $entries) {
    $writer.Write([byte[]]$entry.Bytes, 0, $entry.Bytes.Length)
}

$writer.Flush(); $file.Close()

foreach ($size in $sizes) { $frames[$size].Dispose() }
$source.Dispose()

# Read back rather than trust the write. An icon whose directory disagrees with
# its contents still opens in some viewers and fails in the shell.
$written = [System.IO.File]::ReadAllBytes($icoPath)
$count = [BitConverter]::ToUInt16($written, 4)
$last = 6 + (($count - 1) * 16)
$end = [BitConverter]::ToInt32($written, $last + 8) + [BitConverter]::ToInt32($written, $last + 12)

if ($count -ne $entries.Count -or $end -ne $written.Length) {
    throw "The icon is malformed: $count frames, data ends at $end, file is $($written.Length) bytes."
}

Write-Host "Wrote $icoPath"
Write-Host "  $count frames, $([Math]::Round($written.Length / 1kb, 1)) KB"
Write-Host '  16 and 32 drawn; 48 and above resampled from the master'
