param(
    [string]$DevelopRoot = "e:\game_dev\top_dog_unity",
    [string]$MainRoot = "e:\game_dev\top_dog-main"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $DevelopRoot)) {
    throw "Develop root not found: $DevelopRoot"
}

$excludeDirs = @(
    "Library", "Logs", "Temp", "obj", "bin", "UserSettings",
    ".git", ".cursor", ".vs", "docs", "TestResults"
)
$excludeFiles = @("*.log", "1.txt", "*.xlsx")

function Should-SkipPath([string]$fullPath) {
    foreach ($d in $excludeDirs) {
        if ($fullPath -match "[\\/]$([regex]::Escape($d))([\\/]|$)") {
            return $true
        }
    }
    return $false
}

function Copy-Tree {
    param(
        [string]$RelativePath,
        [string[]]$ExtraSkipFiles = @()
    )
    $src = Join-Path $DevelopRoot $RelativePath
    $dst = Join-Path $MainRoot $RelativePath
    if (-not (Test-Path $src)) {
        Write-Host "Skip missing: $RelativePath"
        return
    }
    New-Item -ItemType Directory -Force -Path $dst | Out-Null
    Get-ChildItem -Path $src -Recurse -Force -File | ForEach-Object {
        $rel = $_.FullName.Substring($src.Length).TrimStart('\', '/')
        if (Should-SkipPath $_.FullName) { return }
        foreach ($pat in ($excludeFiles + $ExtraSkipFiles)) {
            if ($_.Name -like $pat) { return }
        }
        $target = Join-Path $dst $rel
        $parent = Split-Path $target -Parent
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
        Copy-Item -Force $_.FullName $target
    }
    Write-Host "Copied $RelativePath"
}

Write-Host "Publish develop -> main archive"
Write-Host "  From: $DevelopRoot"
Write-Host "  To:   $MainRoot"

foreach ($rel in @("src", "tests", "content", "TopDog.Unity")) {
    Copy-Tree $rel
}

# scripts: exclude doc-sync helper
Copy-Tree "scripts" -ExtraSkipFiles @("sync_docs_from_libgdx.ps1")

$sln = Join-Path $DevelopRoot "TopDog.sln"
if (Test-Path $sln) {
    Copy-Item -Force $sln (Join-Path $MainRoot "TopDog.sln")
    Write-Host "Copied TopDog.sln"
}

# Ensure Core mirror is fresh inside Unity Assets
$sync = Join-Path $MainRoot "scripts\sync_core_to_unity.ps1"
if (Test-Path $sync) {
    & $sync
}

$csDevelop = (Get-ChildItem (Join-Path $DevelopRoot "src") -Recurse -Filter *.cs -File | Measure-Object).Count
$csMain = (Get-ChildItem (Join-Path $MainRoot "src") -Recurse -Filter *.cs -File | Measure-Object).Count
Write-Host "Done. src/*.cs develop=$csDevelop main=$csMain (docs/ excluded by policy)"
