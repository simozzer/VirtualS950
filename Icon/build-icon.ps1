<#
    Build the application icon from the two drawings beside this script.

        .\build-icon.ps1            AkaiS950.ico, icon-1024.png, icon-128.png
        .\build-icon.ps1 -Sheet     and a magnified contact sheet to judge them by

    WHY THERE IS A SCRIPT AND NOT A CHECKED-IN .ICO

    The same reason the disks are generated rather than committed: the .ico is an artefact,
    the SVGs are the source, and a binary nobody can diff drifts from the drawing it came
    from the first time anybody touches either. Rebuild it and the two cannot disagree.

    WHAT IT NEEDS

    Chrome or Edge, for rendering SVG, and System.Drawing, which is in the .NET Framework
    already on the machine. No ImageMagick, no Inkscape, no package to install - the same
    bargain the rest of this repository makes.

    WHY IT CROPS RATHER THAN TRUSTING --window-size

    Headless Chrome does not always give a viewport the size that was asked for, and when it
    does not, the page is laid out at one size and captured at another - which quietly
    produces a screenshot of the top-left corner of a too-large icon. Since that failure
    looks like a rendering bug rather than a tooling one, the icon is instead drawn at an
    exact pixel size inside a comfortably larger window and the corner is cropped out
    deliberately, which cannot be got wrong.
#>
param(
    [switch]$Sheet
)

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$work = Join-Path $env:TEMP "s950icon"

Add-Type -AssemblyName System.Drawing

New-Item -ItemType Directory -Force -Path $work | Out-Null

# ------------------------------------------------------------------ the renderer

$browser = @(
    "$env:ProgramFiles\Google\Chrome\Application\chrome.exe",
    "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe",
    "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe",
    "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $browser) { Write-Error "No Chrome or Edge found to render the SVG with." }

function Render([string]$svg, [int]$size, [string]$out) {
    $page = Join-Path $work "wrap-$size.html"
    $src  = ($svg -replace '\\', '/')

    # An exact pixel size, no margins, and nothing else on the page.
    @"
<!doctype html><meta charset="utf-8">
<style>html,body{margin:0;padding:0;background:transparent;overflow:hidden}
img{display:block;width:${size}px;height:${size}px}</style>
<img src="file:///$src">
"@ | Set-Content -Path $page -Encoding UTF8

    $shot = Join-Path $work "shot-$size.png"
    Remove-Item $shot -ErrorAction SilentlyContinue

    # A window big enough that no minimum-size rule can interfere and the icon always fits
    # inside it, and a virtual time budget so the capture waits for the SVG to have been
    # loaded and painted.
    $window = [math]::Max(800, $size + 80)

    $switches = @(
        "--headless=new", "--disable-gpu", "--no-sandbox", "--hide-scrollbars",
        "--force-device-scale-factor=1", "--default-background-color=00000000",
        "--virtual-time-budget=4000", "--window-size=$window,$window",
        "--user-data-dir=$work\profile", "--allow-file-access-from-files",
        "--screenshot=$shot", "file:///$($page -replace '\\','/')"
    )

    Start-Process -FilePath $browser -ArgumentList $switches -Wait -NoNewWindow | Out-Null
    if (-not (Test-Path $shot)) { Write-Error "The browser wrote no screenshot for $size px." }

    # Take the corner the icon was drawn into.
    $full = [System.Drawing.Image]::FromFile($shot)
    try {
        if ($full.Width -lt $size -or $full.Height -lt $size) {
            Write-Error "Screenshot came back $($full.Width)x$($full.Height), smaller than the $size px wanted."
        }

        $cut = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($cut)
        $g.Clear([System.Drawing.Color]::Transparent)
        $g.DrawImage($full, (New-Object System.Drawing.Rectangle 0, 0, $size, $size),
                            (New-Object System.Drawing.Rectangle 0, 0, $size, $size),
                            [System.Drawing.GraphicsUnit]::Pixel)
        $g.Dispose()
        $cut.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
        $cut.Dispose()
    }
    finally { $full.Dispose() }
}

<#
    Is there an icon in there at all?

    A blank or nearly blank PNG is what a rendering that went wrong looks like, and it would
    sail through every later step to become an .ico of nothing. So each one is measured:
    how much of it is opaque, and how many distinct tones it holds. An icon that is 2% ink
    or a single flat colour is not an icon.
#>
function Inspect([string]$png) {
    $bmp = [System.Drawing.Bitmap]::new($png)
    try {
        $opaque = 0; $total = $bmp.Width * $bmp.Height
        $tones = @{}

        for ($y = 0; $y -lt $bmp.Height; $y++) {
            for ($x = 0; $x -lt $bmp.Width; $x++) {
                $c = $bmp.GetPixel($x, $y)
                if ($c.A -gt 128) {
                    $opaque++
                    $tones[[int]($c.R / 32) * 1000 + [int]($c.G / 32) * 100 + [int]($c.B / 32)] = $true
                }
            }
        }

        [pscustomobject]@{
            Size   = $bmp.Width
            Filled = [math]::Round(100 * $opaque / $total)
            Tones  = $tones.Count
        }
    }
    finally { $bmp.Dispose() }
}

# ---------------------------------------------------------------- the .ico itself

<#
    Pack the PNGs into an .ico.

    The container is a six-byte header, a sixteen-byte directory entry per image, and then
    the images. Each image is stored one of two ways:

      - 128 and 256 as PNG, which is what keeps the file to tens of kilobytes rather than
        a quarter of a megabyte. Windows has understood PNG inside .ico since Vista.

      - everything smaller as a DIB, because that is what every consumer of an .ico has
        always accepted, including the parts of .NET Framework that read one back out of an
        exe. A DIB here is a 40-byte header whose height is doubled, the pixels bottom-up in
        BGRA, and then an AND mask - which is left at zeros, the alpha channel having
        already said what is transparent.
#>
function Dib([string]$png) {
    $bmp = [System.Drawing.Bitmap]::new($png)
    try {
        $w = $bmp.Width; $h = $bmp.Height
        $maskStride = [int][math]::Floor(($w + 31) / 32) * 4

        $out = New-Object System.IO.MemoryStream
        $bw  = New-Object System.IO.BinaryWriter($out)

        $bw.Write([uint32]40)          # biSize
        $bw.Write([int32]$w)           # biWidth
        $bw.Write([int32]($h * 2))     # biHeight - colour and mask together
        $bw.Write([uint16]1)           # biPlanes
        $bw.Write([uint16]32)          # biBitCount
        $bw.Write([uint32]0)           # BI_RGB
        $bw.Write([uint32]($w * $h * 4 + $maskStride * $h))
        $bw.Write([int32]0); $bw.Write([int32]0)
        $bw.Write([uint32]0); $bw.Write([uint32]0)

        $rect = New-Object System.Drawing.Rectangle 0, 0, $w, $h
        $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
                              [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $row = New-Object byte[] ($w * 4)
            for ($y = $h - 1; $y -ge 0; $y--) {       # bottom-up, as a DIB is
                [System.Runtime.InteropServices.Marshal]::Copy(
                    [IntPtr]::Add($data.Scan0, $y * $data.Stride), $row, 0, $row.Length)
                $bw.Write($row)
            }
        }
        finally { $bmp.UnlockBits($data) }

        $bw.Write((New-Object byte[] ($maskStride * $h)))   # AND mask: all zero
        $bw.Flush()
        return $out.ToArray()
    }
    finally { $bmp.Dispose() }
}

function WriteIco([hashtable]$images, [string]$path) {
    $sizes = $images.Keys | Sort-Object
    $blobs = @{}

    #
    # Cast, and keep casting.
    #
    # A byte[] returned from a function or an if comes back as Object[], and handed to
    # BinaryWriter.Write that picks an overload which writes almost nothing - leaving a
    # directory full of correct lengths in front of nine bytes of data. The .ico was 159
    # bytes and looked structurally fine.
    #
    foreach ($s in $sizes) {
        $raw = if ($s -ge 128) { [System.IO.File]::ReadAllBytes($images[$s]) }
               else            { Dib $images[$s] }

        $blobs[$s] = [byte[]]$raw
    }

    $out = New-Object System.IO.MemoryStream
    $bw  = New-Object System.IO.BinaryWriter($out)

    $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)

    $offset = 6 + 16 * $sizes.Count
    foreach ($s in $sizes) {
        $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))   # 0 means 256
        $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
        $bw.Write([byte]0)             # no colour table
        $bw.Write([byte]0)             # reserved
        $bw.Write([uint16]1)           # planes
        $bw.Write([uint16]32)          # bits per pixel
        $bw.Write([uint32]$blobs[$s].Length)
        $bw.Write([uint32]$offset)
        $offset += $blobs[$s].Length
    }

    foreach ($s in $sizes) { $bw.Write($blobs[$s], 0, $blobs[$s].Length) }

    $bw.Flush()
    [System.IO.File]::WriteAllBytes($path, $out.ToArray())
    $bw.Dispose()
}

# ------------------------------------------------------------------------- build

$small = Join-Path $here "icon-small.svg"
$big   = Join-Path $here "icon.svg"

# Where the two drawings hand over. Below 40 the detailed one is mud; at 40 and above the
# simplified one is empty. The pair share their outer geometry so the swap is invisible.
$fromSmall = 16, 20, 24, 32
$fromBig   = 40, 48, 64, 128, 256

$pngs = @{}

Write-Host ""
Write-Host "  rendering"

foreach ($s in $fromSmall + $fromBig) {
    $svg = if ($fromSmall -contains $s) { $small } else { $big }
    $out = Join-Path $work "icon-$s.png"

    Render $svg $s $out
    $seen = Inspect $out

    # A rendering that went wrong is blank, or one flat colour where a panel should be.
    if ($seen.Filled -lt 25 -or $seen.Tones -lt 3) {
        Write-Error ("$s px came out $($seen.Filled)% filled with $($seen.Tones) tones - " +
                     "that is not an icon. The browser rendered nothing useful.")
    }

    $pngs[$s] = $out
    Write-Host ("    {0,4} px   {1,3}% filled, {2,2} tones   {3}" -f `
                $s, $seen.Filled, $seen.Tones, (Split-Path $svg -Leaf))
}

$ico = Join-Path $here "AkaiS950.ico"
WriteIco $pngs $ico

# JUCE takes PNGs rather than an .ico and builds the Windows resource itself.
Render $big 1024 (Join-Path $here "icon-1024.png")
Copy-Item $pngs[128] (Join-Path $here "icon-128.png") -Force

Write-Host ""
Write-Host ("  {0}  -  {1:N0} bytes, {2} sizes" -f (Split-Path $ico -Leaf), (Get-Item $ico).Length, $pngs.Count)
Write-Host "  icon-1024.png and icon-128.png  -  for the plugin"

# ------------------------------------------------------------- the contact sheet

if ($Sheet) {
    $shown = 16, 24, 32, 40, 48, 64
    $cell = 150

    $board = [System.Drawing.Bitmap]::new($cell * $shown.Count, 210)
    $g = [System.Drawing.Graphics]::FromImage($board)
    $g.Clear([System.Drawing.Color]::FromArgb(255, 244, 244, 246))
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::Half

    $font = [System.Drawing.Font]::new("Segoe UI", 12)
    $x = 0

    foreach ($s in $shown) {
        $bmp = [System.Drawing.Image]::FromFile($pngs[$s])
        $scale = [math]::Floor(128 / $s); $w = $s * $scale

        $g.DrawImage($bmp, [int]($x + ($cell - $w) / 2), [int](20 + (128 - $w) / 2), [int]$w, [int]$w)
        $g.DrawString("$s px", $font, [System.Drawing.Brushes]::Black,
                      [single]($x + $cell / 2 - 22), [single]170)
        $bmp.Dispose()
        $x += $cell
    }

    $g.Dispose()
    $path = Join-Path $work "contact-sheet.png"
    $board.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $board.Dispose()

    Write-Host "  contact sheet  -  $path"
}

Write-Host ""
