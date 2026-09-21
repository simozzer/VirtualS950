<#
    Build the disk generator.

        .\build.ps1                 builds AkaiS950Synth.exe beside this script
        .\build.ps1 -List           builds it and prints the library it would write
        .\build.ps1 -To <folder>    builds it and writes the disks there

    WHY IT HAS ITS OWN BUILD

    This is not part of the Studio application and does not belong in it. The Studio
    reads, edits and plays disks somebody already has; this writes new ones out of
    nothing, has no window, no audio, and no reason to be carried around by a program
    that does. It is a workshop tool that happens to live in the same repository
    because it needs the same disk format.

    So it builds alone, and the only thing it shares is AkaiS950List - the disk reader
    and writer. Nothing from AkaiS950Engine, nothing from AkaiS950Studio, no WinForms:
    if this ever starts needing those, something has gone in the wrong direction.

    csc from .NET Framework 4.0, for the reason given in the Studio's build.ps1: there
    is no SDK on this machine, and C# 5 is what the source is written to.
#>
param(
    [string]$To = "",
    [switch]$List
)

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = Split-Path -Parent $here

$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    $csc = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
}
if (-not (Test-Path $csc)) {
    Write-Error "No .NET Framework 4.0 compiler found. Looked in $env:WINDIR\Microsoft.NET."
}

$out = Join-Path $here "AkaiS950Synth.exe"

# Program.cs in the List project is the inventory tool's entry point, and two Mains in
# one assembly is an error rather than a choice.
$src = @()
$src += Get-ChildItem (Join-Path $repo "AkaiS950List\*.cs") |
        Where-Object { $_.Name -ne "Program.cs" } | ForEach-Object { $_.FullName }
$src += Get-ChildItem (Join-Path $here "*.cs") | ForEach-Object { $_.FullName }

Write-Host "building $($src.Count) files -> $out"

& $csc /nologo /target:exe /out:$out /r:System.dll /r:System.Core.dll $src
if ($LASTEXITCODE -ne 0) { Write-Error "build failed" }

Write-Host "ok  -  $([math]::Round((Get-Item $out).Length / 1024)) KB"

if ($List) { & $out --list }
elseif ($To -ne "") { & $out $To }
