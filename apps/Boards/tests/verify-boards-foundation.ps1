$ErrorActionPreference = 'Stop'

$boards = Split-Path -Parent $PSScriptRoot
$root = Split-Path -Parent (Split-Path -Parent $boards)

$pubspec = Join-Path $boards 'appflowy_poc/pubspec.yaml'
$thirdParty = Join-Path $boards 'THIRD_PARTY.md'
$contract = Join-Path $boards 'contract/HavenBoardContract.cs'
$hui = Join-Path $boards 'hui/HavenBoardsHuiScene.cs'
$harness = Join-Path $boards 'appflowy_poc/lib/main.dart'

$required = @($pubspec, $thirdParty, $contract, $hui, $harness)
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
if ($contractText -match 'AppFlowy|Flutter|Avalonia') {
    throw 'Neutral Haven board contract contains a renderer/vendor dependency.'
}

$huiText = Get-Content -LiteralPath $hui -Raw
if ($huiText -match 'AppFlowy|Flutter|Avalonia') {
    throw 'HUI board scene crossed the renderer/vendor boundary.'
}
if ($huiText -notmatch 'HavenBoardCommand') {
    throw 'HUI board scene must emit typed Haven board commands.'
}

$harnessText = Get-Content -LiteralPath $harness -Raw
if ($harnessText -notmatch 'package:appflowy_board/appflowy_board.dart') {
    throw 'Flutter proof harness is not using appflowy-board.'
}

Write-Host 'Haven Boards AppFlowy foundation static checks passed.'
