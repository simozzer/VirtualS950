<#
    Run the checks.

        .\test.ps1              everything that needs nothing but the repo
        .\test.ps1 -Audio       also open the sound card and play a chord
        .\test.ps1 -Images D:\  also play every programme in a disk library

    The three of them answer different questions and none of them substitutes for another.
    EngineCheck is the maths, against the numbers the web version prints for the same
    inputs. LoopClickCheck is the loop join, measured as a step against the steps the
    waveform makes on its own. PatchCheck is real Akai programmes, which is where the
    surprises live. AudioCheck is the only one that can tell you whether the WASAPI vtable
    is in the right order, because that is not a question a buffer can answer.
#>
param(
    [switch]$Audio,
    [string]$Images = "",
    [switch]$KeepGoing
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe" }
if (-not (Test-Path $csc)) { Write-Error "No .NET Framework 4.0 compiler found." }

$engine = Get-ChildItem (Join-Path $root "AkaiS950Engine\*.cs") | ForEach-Object { $_.FullName }
$list   = Get-ChildItem (Join-Path $root "AkaiS950List\*.cs") |
          Where-Object { $_.Name -ne "Program.cs" } | ForEach-Object { $_.FullName }
$studio = Join-Path $root "AkaiS950Studio\Instrument.cs"

# The whole editor, for the check that drives its tree. Program.cs brings a second Main
# with it, which is why Run-Check passes /main.
$studioAll = Get-ChildItem (Join-Path $root "AkaiS950Studio\*.cs") | ForEach-Object { $_.FullName }

$failed = @()

function Run-Check($name, $sources, $arguments) {
    Write-Host ""
    Write-Host ("--- $name " + ("-" * [math]::Max(0, 60 - $name.Length))) -ForegroundColor Cyan

    $exe = Join-Path $env:TEMP "$name.exe"
    & $csc /nologo /unsafe /target:exe /main:$name /out:$exe `
        /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll $sources
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  did not compile" -ForegroundColor Red
        $script:failed += $name
        return
    }

    if ($arguments) { & $exe $arguments } else { & $exe }
    if ($LASTEXITCODE -ne 0) { $script:failed += $name }
}

Run-Check "EngineCheck" (@(Join-Path $root "AkaiS950Tests\EngineCheck.cs") + $engine) $null
Run-Check "LoopClickCheck" (@(Join-Path $root "AkaiS950Tests\LoopClickCheck.cs") + $engine) $null
Run-Check "PurityCheck" (@(Join-Path $root "AkaiS950Tests\PurityCheck.cs") + $engine) $null

# This one drives the real keyboard control through its own mouse handlers, because the
# bug it guards lives in the bookkeeping between a press and a release. So it needs
# WinForms and the control itself.
Run-Check "StuckNoteCheck" (@(Join-Path $root "AkaiS950Tests\StuckNoteCheck.cs") + $engine + @(Join-Path $root "AkaiS950Studio\PianoKeyboard.cs")) $null

# The WAV export, against the disk library in the repository - which is always there, so
# unlike PatchCheck this one needs no -Images. The web version writes the same file from
# the same disks; test\wavtest.js over in AkaiS950Web is the other half of this pair.
Run-Check "WavCheck" `
    (@(Join-Path $root "AkaiS950Tests\WavCheck.cs") + $list + @(Join-Path $root "AkaiS950Studio\WavFile.cs")) `
    (Join-Path $root "disks")

# Copying between disks, across every pair in the library. This one writes, so most of
# what it asserts is about what the copy must NOT disturb: the target's own files, and
# the pointers the sampler reads. Also needs no -Images.
Run-Check "CopyCheck" (@(Join-Path $root "AkaiS950Tests\CopyCheck.cs") + $list) (Join-Path $root "disks")

# And the same for a keygroup, which is not a file: the target program grows in place and
# the arena moves under every program on the disk, so the pointer check is the point.
Run-Check "KeygroupCopyCheck" `
    (@(Join-Path $root "AkaiS950Tests\KeygroupCopyCheck.cs") + $list) (Join-Path $root "disks")

# The velocity strip beside the keyboard, driven through its own mouse handlers. It needs
# only the one control, which is why it is not carrying the whole editor with it.
Run-Check "VelocityCheck" `
    (@(Join-Path $root "AkaiS950Tests\VelocityCheck.cs") +
     @(Join-Path $root "AkaiS950Studio\VelocitySlider.cs")) $null

# The tree keeping its shape across a rebuild. Like StuckNoteCheck this drives the real
# control rather than a copy of it, so it needs WinForms and the editor itself.
Run-Check "TreeStateCheck" `
    (@(Join-Path $root "AkaiS950Tests\TreeStateCheck.cs") + $engine + $list + $studioAll) `
    (Join-Path $root "disks")

if ($Images -ne "") {
    Run-Check "PatchCheck" `
        (@(Join-Path $root "AkaiS950Tests\PatchCheck.cs") + $engine + $studio + $list) $Images

    # Needs a real programme to edit, so it belongs here rather than above.
    Run-Check "EditHeardCheck" `
        (@(Join-Path $root "AkaiS950Tests\EditHeardCheck.cs") + $engine + $studio + $list) $Images
} else {
    Write-Host ""
    Write-Host "(PatchCheck skipped - pass -Images <folder of .hfe> to play the library)"
}

if ($Audio) {
    Run-Check "AudioCheck" (@(Join-Path $root "AkaiS950Tests\AudioCheck.cs") + $engine) $null
} else {
    Write-Host ""
    Write-Host "(AudioCheck skipped - pass -Audio to open the sound card)"
}

Write-Host ""
if ($failed.Count -eq 0) {
    Write-Host "all checks passed" -ForegroundColor Green
    exit 0
}

Write-Host ("failed: " + ($failed -join ", ")) -ForegroundColor Red
exit 1
