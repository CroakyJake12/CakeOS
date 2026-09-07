$ErrorActionPreference = 'Stop'

$boards = Split-Path -Parent $PSScriptRoot

$pubspec = Join-Path $boards 'appflowy_poc/pubspec.yaml'
$thirdParty = Join-Path $boards 'THIRD_PARTY.md'
$contract = Join-Path $boards 'contract/HavenBoardContract.cs'
$contractProject = Join-Path $boards 'contract/CakeOS.Apps.Boards.Contract.csproj'
$store = Join-Path $boards 'contract/JsonFileHavenBoardStore.cs'
$hui = Join-Path $boards 'hui/HavenBoardsHuiScene.cs'
$harness = Join-Path $boards 'appflowy_poc/lib/main.dart'
$testProject = Join-Path $boards 'tests/CakeOS.Apps.Boards.Tests.csproj'
$contractTests = Join-Path $boards 'tests/HavenBoardContractTests.cs'

$required = @(
    $pubspec,
    $thirdParty,
    $contract,
    $contractProject,
    $store,
    $hui,
    $harness,
    $testProject,
    $contractTests
)
$missing = $required | Where-Object { -not (Test-Path -LiteralPath $_) }
if ($missing) { throw "Missing Haven Boards foundation files: $($missing -join ', ')" }

$expectedCommit = '804d7898ac0becabf73e45527baf5d5c573cd6bb'
$pubspecText = Get-Content -LiteralPath $pubspec -Raw
if ($pubspecText -notmatch [regex]::Escape($expectedCommit)) {
    throw "appflowy-board is not pinned to expected commit $expectedCommit"
}
if ($pubspecText -match '(?m)^\s*ref:\s*(main|master)\s*$') {
    throw 'appflowy-board must not track a moving branch.'
}

$thirdPartyText = Get-Content -LiteralPath $thirdParty -Raw
if ($thirdPartyText -notmatch 'MPL-?2\.0|Mozilla Public License 2\.0') {
    throw 'MPL 2.0 selection/provenance is missing from THIRD_PARTY.md.'
}

$contractText = Get-Content -LiteralPath $contract -Raw
if ($contractText -match '(?mi)^\s*using\s+(AppFlowy|Avalonia)\b|package:(appflowy_board|flutter)') {
    throw 'Neutral Haven board contract contains a renderer/vendor import.'
}

$huiText = Get-Content -LiteralPath $hui -Raw
if ($huiText -match '(?mi)^\s*using\s+(AppFlowy|Avalonia)\b|package:(appflowy_board|flutter)') {
    throw 'HUI board scene crossed the renderer/vendor import boundary.'
}
if ($huiText -notmatch '(?m)^using Haven\.UI;') {
    throw 'HUI board scene is not targeting the Haven UI runtime.'
}
if ($huiText -notmatch 'HavenBoardCommand') {
    throw 'HUI board scene must emit typed Haven board commands.'
}

$storeText = Get-Content -LiteralPath $store -Raw
if ($storeText -notmatch '\.json\.bak' -or $storeText -notmatch 'flushToDisk:\s*true') {
    throw 'Local-first store must retain backup recovery and durable flush semantics.'
}
if ($storeText -match 'HttpClient|WebSocket|https?://') {
    throw 'Local-first board store must not contain a network dependency.'
}

$harnessText = Get-Content -LiteralPath $harness -Raw
if ($harnessText -notmatch 'package:appflowy_board/appflowy_board.dart') {
    throw 'Flutter proof harness is not using appflowy-board.'
}

$testProjectText = Get-Content -LiteralPath $testProject -Raw
if ($testProjectText -notmatch 'CakeOS\.Apps\.Boards\.Contract\.csproj' -or $testProjectText -notmatch 'xunit') {
    throw 'Executable contract test project is not wired to the board contract.'
}

Write-Host 'Haven Boards AppFlowy foundation static checks passed.'
