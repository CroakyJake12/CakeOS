#!/usr/bin/env pwsh

$root = (Get-Item -LiteralPath "$PSScriptRoot/..").FullName
$lock = Join-Path $root "havenos.lock"
$dest = Join-Path $root "HUI/vendor/Haven.UI"

if (-not (Get-Command git -ErrorAction SilentlyContinue)) { Write-Error "git is required"; exit 2 }
if (-not (Get-Command jq -ErrorAction SilentlyContinue)) { Write-Error "jq is required"; exit 2 }
if (-not (Test-Path -LiteralPath $lock)) { Write-Error "Missing havenos.lock"; exit 2 }

$repo = jq -r '.components.HUI.migrationDonor.repository' $lock
$donorRev = jq -r '.components.HUI.migrationDonor.revision' $lock
$sourcePath = jq -r '.components.HUI.migrationDonor.sourcePath' $lock

if (-not ($repo -match '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')) { Write-Error "Invalid HUI donor repository"; exit 2 }
if (-not ($donorRev -match '^[0-9a-f]{40}$')) { Write-Error "HUI donor revision must be a full commit SHA"; exit 2 }
if (-not ($sourcePath -and $sourcePath -ne 'null' -and -not $sourcePath.StartsWith('/') -and -not $sourcePath.Contains('..'))) { Write-Error "Invalid HUI donor source path"; exit 2 }

$tmp = [System.IO.Path]::GetTempFileName()
Remove-Item -LiteralPath $tmp -Force
New-Item -ItemType Directory -Path $tmp | Out-Null
try {
    git -C $tmp init -q
    git -C $tmp remote add origin "https://github.com/$repo.git"
    git -C $tmp fetch -q --depth 1 origin $donorRev
    git -C $tmp checkout -q --detach FETCH_HEAD
    $actual = git -C $tmp rev-parse HEAD
    if ($actual -ne $donorRev) { Write-Error "Fetched donor SHA $actual does not match lock $donorRev"; exit 1 }
    $sourceFullPath = [System.IO.Path]::Combine($tmp, $sourcePath)
    if (-not (Test-Path -LiteralPath (Join-Path $sourceFullPath "Haven.UI.csproj"))) { Write-Error "Pinned donor does not contain $sourcePath/Haven.UI.csproj"; exit 1 }

    if (Test-Path -LiteralPath $dest) { Remove-Item -LiteralPath $dest -Recurse -Force }
    New-Item -ItemType Directory -Path $dest | Out-Null
    Copy-Item -LiteralPath $sourceFullPath -Destination $dest -Recurse

    # The pinned donor currently contains tracked build-output trees such as obj-hui.
    # Generated output is not source and must never cross the CakeOS migration boundary.
    Get-ChildItem -LiteralPath $dest -Recurse -Directory | Where-Object { $_.Name -match '^(obj|obj-|bin|bin-)$' } | Remove-Item -Recurse -Force
    if (Get-ChildItem -LiteralPath $dest -Recurse -Directory | Where-Object { $_.Name -match '^(obj|obj-|bin|bin-)$' }) { Write-Error "Generated build-output directory remained after donor staging"; exit 1 }

    $donorRev | Set-Content -LiteralPath (Join-Path $root "HUI/vendor/.donor-revision") -NoNewline
    $msg = "Staged source-only $repo@$donorRev" + ":" + "$sourcePath into HUI/vendor/Haven.UI"
    Write-Host $msg
}
finally {
    if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Recurse -Force }
}