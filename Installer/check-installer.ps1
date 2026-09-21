<#
    Read the Inno script and check it against the files it names.

        .\check-installer.ps1

    WHAT THIS IS FOR

    Compiling the installer needs Inno Setup. This does not - it parses the .iss and checks
    the things that are wrong most often and that a compile would not always catch anyway:
    a Source that does not exist, a shortcut pointing at a file nothing installs, a
    Components: or Tasks: name that does not match anything declared.

    ISCC catches the first of those. It does NOT catch a Start Menu entry aimed at a file
    that is never copied, because {app}\Whatever.exe is a perfectly well-formed path to
    something that will not be there - you find that out from a broken shortcut after
    installing. So this is worth running even once Inno is on the machine.
#>
param(
    [string]$Script = ""
)

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if ($Script -eq "") { $Script = Join-Path $here "VirtualS950.iss" }

$raw = Get-Content $Script

#
# Inno continues a long entry onto the next line with a trailing backslash. Read line by
# line and a continued entry arrives as two, one of which has no Source and half the
# fields - so the checks run against a fragment and pass for the wrong reason. Join them
# back together first.
#
$lines = @()
$pending = ""

foreach ($l in $raw) {
    $t = $l.TrimEnd()

    if ($t.EndsWith("\")) {
        $pending += $t.Substring(0, $t.Length - 1).TrimEnd() + " "
        continue
    }

    $lines += ($pending + $t)
    $pending = ""
}

if ($pending -ne "") { $lines += $pending }

# ---------------------------------------------------------------- the #defines

#
# Inno's preprocessor evaluates a #define as an expression, not as a literal, so
#
#     #define PluginArtefacts RepoRoot + "\Plugin\build\..."
#
# is a string concatenation of an earlier define and a literal. Treating the whole
# right-hand side as text - which is what this did first - reports two perfectly good paths
# as missing, and the mistake looks exactly like a broken script.
#
$defines = [ordered]@{}

foreach ($l in $lines) {
    if ($l -notmatch '^\s*#define\s+(\w+)\s+(.+?)\s*$') { continue }

    $name = $Matches[1]
    $expr = $Matches[2]

    $value = ""
    foreach ($piece in ($expr -split '\s*\+\s*')) {
        $p = $piece.Trim()

        if ($p.StartsWith('"') -and $p.EndsWith('"')) {
            $value += $p.Trim('"')                      # a literal
        } elseif ($defines.Contains($p)) {
            $value += $defines[$p]                      # an earlier define
        } else {
            $value += $p                                # something this does not model
        }
    }

    $defines[$name] = $value
}

function Expand($text) {
    $out = $text
    foreach ($k in $defines.Keys) { $out = $out.Replace("{#$k}", $defines[$k]) }
    return $out
}

# ------------------------------------------------------------------ the sections

$section = ""
$files = @(); $components = @(); $tasks = @(); $icons = @(); $runs = @()

foreach ($l in $lines) {
    $t = $l.Trim()
    if ($t -match '^\[(\w+)\]$') { $section = $Matches[1]; continue }
    if ($t -eq "" -or $t.StartsWith(";")) { continue }

    switch ($section) {
        "Files"      { $files      += $t }
        "Components" { $components += $t }
        "Tasks"      { $tasks      += $t }
        "Icons"      { $icons      += $t }
        "Run"        { $runs       += $t }
    }
}

function Field($entry, $name) {
    if ($entry -match "(?i)\b$name\s*:\s*`"?([^`";]+)`"?") { return $Matches[1].Trim() }
    return $null
}

$declaredComponents = @()
foreach ($c in $components) { $n = Field $c "Name"; if ($n) { $declaredComponents += $n } }

$declaredTasks = @()
foreach ($c in $tasks) { $n = Field $c "Name"; if ($n) { $declaredTasks += $n } }

Write-Host ""
Write-Host "  $Script" -ForegroundColor Cyan
Write-Host ("  {0} files, {1} components, {2} tasks, {3} icons, {4} run entries" -f `
            $files.Count, $declaredComponents.Count, $declaredTasks.Count, $icons.Count, $runs.Count)
Write-Host ""

$problems = @()

# ------------------------------------------------- the [Setup] files, which are not [Files]
#
# SetupIconFile is a path like any other in here, but it lives in [Setup] where nothing
# above looks, and Inno fails the compile outright if it is missing. Since it names a
# generated artefact, "run the script that makes it" is a far better thing to be told here
# than a compiler error in CI.
#
foreach ($l in $lines) {
    if ($l.Trim() -notmatch '^(?i)(SetupIconFile|WizardImageFile|WizardSmallImageFile|LicenseFile)\s*=\s*(.+)$') { continue }

    $what = $Matches[1]
    $path = Join-Path $here (Expand $Matches[2].Trim())

    if (Test-Path $path) {
        Write-Host ("    ok    {0} -> {1}" -f $what, (Split-Path $path -Leaf)) -ForegroundColor Green
    } else {
        Write-Host ("    GONE  {0} -> {1}" -f $what, $path) -ForegroundColor Red
        $problems += "$what names $path, which is not there - Inno will refuse to compile"
    }
}

# ------------------------------------------------- every Source has to exist

$installed = @()      # what ends up in {app}, so shortcuts can be checked against it

foreach ($f in $files) {
    $src  = Field $f "Source"
    $dest = Field $f "DestDir"
    if (-not $src) { continue }

    $full = Expand $src
    $path = Join-Path $here ($full -replace '\\\*$', '')
    $path = $path -replace '\\\*$', ''

    $exists = Test-Path $path

    if ($exists) {
        Write-Host ("    ok    {0}" -f $full) -ForegroundColor Green
    } else {
        Write-Host ("    GONE  {0}" -f $full) -ForegroundColor Red
        $problems += "Source does not exist: $full"
    }

    # Remember what lands in {app}, under whatever name it arrives as - and where.
    #
    # The path is kept relative to {app} rather than just the leaf, because a shortcut
    # asks for "docs\tutorial.html" while the Files entry that installs it says
    # DestDir {app}\docs and Source ...\tutorial.html. Matching on the name alone called
    # a perfectly good shortcut dead, which is the one thing a check like this must not do.
    if ($dest -and $dest -match '\{app\}') {
        $name = Field $f "DestName"
        if (-not $name) { $name = Split-Path $full -Leaf }

        $sub = ($dest -replace '^\s*\{app\}', '').Trim('\').Trim()
        if ($sub) { $installed += "$sub\$name" } else { $installed += $name }
    }

    # Components on a Files entry must be declared.
    $comp = Field $f "Components"
    if ($comp) {
        foreach ($c in ($comp -split '\s+')) {
            if ($c -and $declaredComponents -notcontains $c) {
                $problems += "Files entry names an undeclared component '$c'"
            }
        }
    }
}

# --------------------------------- shortcuts and Run entries must point at something

Write-Host ""

foreach ($group in @(@{ What = "Icons"; Items = $icons }, @{ What = "Run"; Items = $runs })) {
    foreach ($e in $group.Items) {
        $filename = Field $e "Filename"
        if (-not $filename) { continue }

        # {uninstallexe} is made by Setup itself, not copied by us.
        if ($filename -match '\{uninstallexe\}') { continue }

        if ($filename -match '\{app\}\\(.+)$') {
            $leaf = $Matches[1]

            if ($installed -contains $leaf) {
                Write-Host ("    ok    {0} -> {1}" -f $group.What, $leaf) -ForegroundColor Green
            } else {
                Write-Host ("    DEAD  {0} -> {1}" -f $group.What, $leaf) -ForegroundColor Red
                $problems += "$($group.What) points at {app}\$leaf, which no Files entry installs"
            }
        }

        $comp = Field $e "Components"
        if ($comp) {
            foreach ($c in ($comp -split '\s+')) {
                if ($c -and $declaredComponents -notcontains $c) {
                    $problems += "$($group.What) entry names an undeclared component '$c'"
                }
            }
        }

        $task = Field $e "Tasks"
        if ($task -and $declaredTasks -notcontains $task) {
            $problems += "$($group.What) entry names an undeclared task '$task'"
        }
    }
}

# ----------------------------------------------------------------------- verdict

Write-Host ""

if ($problems.Count -eq 0) {
    Write-Host "  nothing wrong that can be found without compiling it" -ForegroundColor Green
    Write-Host ""
    exit 0
}

foreach ($p in $problems) { Write-Host "  $p" -ForegroundColor Red }
Write-Host ""
exit 1
