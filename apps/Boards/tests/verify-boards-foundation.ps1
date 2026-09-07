$ErrorActionPreference = 'Stop'

$boards = Split-Path -Parent $PSScriptRoot

$pubspec = Join-Path $boards 'appflowy_poc/pubspec.yaml'
$thirdParty = Join-Path $boards 'THIRD_PARTY.md'
$contract = Join-Path $boards 'contract/HavenBoardContract.cs'
$contractProject = Join-Path $boards 'contract/CakeOS.Apps.Boards.Contract.csproj'
$store = Join-Path $boards 'contract/JsonFileHavenBoardStore.cs'
$attachmentStore = Join-Path $boards 'contract/ContentAddressedHavenBoardAttachmentStore.cs'
$hui = Join-Path $boards 'hui/HavenBoardsHuiScene.cs'
$huiSession = Join-Path $boards 'hui/HavenBoardsHuiSession.cs'
$huiProject = Join-Path $boards 'hui/CakeOS.Apps.Boards.Hui.csproj'
$huiTestProject = Join-Path $boards 'hui-tests/CakeOS.Apps.Boards.Hui.Tests.csproj'
$huiTests = Join-Path $boards 'hui-tests/HavenBoardsHuiSceneTests.cs'
$harness = Join-Path $boards 'appflowy_poc/lib/main.dart'
$flutterTests = Join-Path $boards 'appflowy_poc/test/appflowy_board_poc_test.dart'
$testProject = Join-Path $boards 'tests/CakeOS.Apps.Boards.Tests.csproj'
$contractTests = Join-Path $boards 'tests/HavenBoardContractTests.cs'

$required = @(
    $pubspec,
    $thirdParty,
    $contract,
    $contractProject,
    $store,
    $attachmentStore,
    $hui,
    $huiSession,
    $huiProject,
    $huiTestProject,
    $huiTests,
    $harness,
    $flutterTests,
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
if ($contractText -notmatch 'AddAttachmentCommand' -or $contractText -notmatch 'RemoveAttachmentCommand') {
    throw 'Neutral Haven board contract must retain typed attachment metadata commands.'
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
if ($huiText -notmatch 'SetState\(HavenElementState\.Disabled,\s*!enabled\)' -or
    $huiText -notmatch 'Accessibility\.Enabled\s*=\s*enabled') {
    throw 'HUI board controls must synchronize Enabled, accessibility, and Disabled state.'
}

$huiSessionText = Get-Content -LiteralPath $huiSession -Raw
if ($huiSessionText -match 'HttpClient|WebSocket|https?://') {
    throw 'Composed HUI session must remain local-only and contain no network dependency.'
}
if ($huiSessionText -notmatch 'Scene\.CommandRequested \+= OnSceneCommandRequested' -or
    $huiSessionText -notmatch 'await _store\.SaveAsync\(updated' -or
    $huiSessionText -notmatch 'Snapshot = updated') {
    throw 'Composed HUI session must bind typed scene commands through durable storage.'
}
$sessionSaveIndex = $huiSessionText.IndexOf('await _store.SaveAsync(updated', [System.StringComparison]::Ordinal)
$sessionPublishIndex = $huiSessionText.IndexOf('Snapshot = updated', [System.StringComparison]::Ordinal)
if ($sessionSaveIndex -lt 0 -or $sessionPublishIndex -lt 0 -or $sessionSaveIndex -gt $sessionPublishIndex) {
    throw 'Composed HUI session must persist a mutation before publishing it as the visible snapshot.'
}

$huiProjectText = Get-Content -LiteralPath $huiProject -Raw
if ($huiProjectText -notmatch 'HavenUiProjectPath' -or
    $huiProjectText -notmatch 'RequireRealHavenUi' -or
    $huiProjectText -notmatch 'Haven\.UI\.csproj') {
    throw 'HUI build project must fail closed unless a real Haven.UI project is supplied.'
}
if ($huiProjectText -match '<PackageReference[^>]+(?:Avalonia|Flutter|AppFlowy)') {
    throw 'HUI product project must not take a renderer/vendor package dependency.'
}

$huiTestProjectText = Get-Content -LiteralPath $huiTestProject -Raw
if ($huiTestProjectText -notmatch 'CakeOS\.Apps\.Boards\.Hui\.csproj' -or
    $huiTestProjectText -notmatch 'xunit') {
    throw 'Executable HUI scene test project is not wired to the real-HUI compile project.'
}

$huiTestsText = Get-Content -LiteralPath $huiTests -Raw
if ($huiTestsText -notmatch 'Disabled_keyboard_move_cannot_emit_command' -or
    $huiTestsText -notmatch 'Enabled_keyboard_move_emits_typed_neutral_command') {
    throw 'HUI tests must cover enabled and disabled keyboard command paths.'
}
if ($huiTestsText -notmatch 'Open_execute_dispose_reopen_preserves_durable_snapshot_and_scene' -or
    $huiTestsText -notmatch 'Keyboard_scene_command_flushes_to_disk_and_survives_reopen') {
    throw 'HUI tests must cover composed durable reopen and keyboard-originated persistence.'
}
if ($huiTestsText -notmatch 'Attachment_blob_metadata_and_hui_count_survive_reopen') {
    throw 'HUI tests must cover attachment blob/metadata survival across reopen.'
}

$storeText = Get-Content -LiteralPath $store -Raw
if ($storeText -notmatch '\.json\.bak' -or $storeText -notmatch 'flushToDisk:\s*true') {
    throw 'Local-first store must retain backup recovery and durable flush semantics.'
}
if ($storeText -match 'HttpClient|WebSocket|https?://') {
    throw 'Local-first board store must not contain a network dependency.'
}

$attachmentStoreText = Get-Content -LiteralPath $attachmentStore -Raw
if ($attachmentStoreText -match 'HttpClient|WebSocket|https?://') {
    throw 'Attachment store must remain local-only and contain no network dependency.'
}
if ($attachmentStoreText -notmatch 'IncrementalHash\.CreateHash\(HashAlgorithmName\.SHA256\)' -or
    $attachmentStoreText -notmatch 'ReferencePrefix = "sha256:"' -or
    $attachmentStoreText -notmatch 'flushToDisk:\s*true') {
    throw 'Attachment store must remain content-addressed and durably flushed.'
}
if ($attachmentStoreText -notmatch 'Path\.GetFileName' -or
    $attachmentStoreText -notmatch 'DefaultMaxAttachmentBytes' -or
    $attachmentStoreText -notmatch 'RejectLinkOrReparsePoint') {
    throw 'Attachment store must retain display-name isolation, bounded imports, and link/reparse protection.'
}

$harnessText = Get-Content -LiteralPath $harness -Raw
if ($harnessText -notmatch 'package:appflowy_board/appflowy_board.dart') {
    throw 'Flutter proof harness is not using appflowy-board.'
}
if ($harnessText -notmatch 'this\.enablePersistence\s*=\s*true') {
    throw 'Flutter proof harness must keep local persistence enabled by default.'
}

$flutterTestsText = Get-Content -LiteralPath $flutterTests -Raw
if ($flutterTestsText -notmatch 'HavenBoardsPocApp\(enablePersistence:\s*false\)') {
    throw 'Flutter widget tests must disable filesystem persistence and test UI behavior deterministically.'
}
if ($flutterTestsText -notmatch 'find\.byType\(AppFlowyBoard\)') {
    throw 'Flutter widget tests must prove the real AppFlowy Board is mounted.'
}

$testProjectText = Get-Content -LiteralPath $testProject -Raw
if ($testProjectText -notmatch 'CakeOS\.Apps\.Boards\.Contract\.csproj' -or $testProjectText -notmatch 'xunit') {
    throw 'Executable contract test project is not wired to the board contract.'
}

$contractTestsText = Get-Content -LiteralPath $contractTests -Raw
if ($contractTestsText -notmatch 'Import_is_content_addressed_deduplicated_and_display_name_cannot_escape_storage' -or
    $contractTestsText -notmatch 'Existing_deduplicated_blob_must_still_match_its_digest') {
    throw 'Contract tests must cover content-addressing/path isolation and tamper detection.'
}

Write-Host 'Haven Boards AppFlowy foundation static checks passed.'
