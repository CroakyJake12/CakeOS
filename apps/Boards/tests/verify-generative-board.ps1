$ErrorActionPreference = 'Stop'

$boards = Split-Path -Parent $PSScriptRoot
$planner = Join-Path $boards 'contract/HavenBoardGenerativePlan.cs'
$coordinator = Join-Path $boards 'hui/HavenBoardGenerativeUiCoordinator.cs'
$session = Join-Path $boards 'hui/HavenBoardsHuiSession.cs'
$plannerTests = Join-Path $boards 'tests/HavenBoardGenerativePlanTests.cs'
$coordinatorTests = Join-Path $boards 'hui-tests/HavenBoardGenerativeUiCoordinatorTests.cs'

$required = @($planner, $coordinator, $session, $plannerTests, $coordinatorTests)
$missing = $required | Where-Object { -not (Test-Path -LiteralPath $_ -PathType Leaf) }
if ($missing) {
    throw "Missing generative Boards safety files: $($missing -join ', ')"
}

$plannerText = Get-Content -LiteralPath $planner -Raw
if ($plannerText -match 'HttpClient|WebSocket|https?://') {
    throw 'Generative planner must remain renderer/provider/network independent.'
}
if ($plannerText -notmatch 'MaxCommandsPerPlan\s*=\s*64' -or
    $plannerText -notmatch 'MaxGeneratedTitleLength' -or
    $plannerText -notmatch 'MaxGeneratedCardIdLength') {
    throw 'Generative planner must retain explicit batch and generated-field bounds.'
}
if ($plannerText -notmatch 'Array\.AsReadOnly\(materialized\)' -or
    $plannerText -notmatch '_\s*=>\s*false') {
    throw 'Generative planner must freeze reviewed commands and fail closed on unknown commands.'
}
if ($plannerText -match '(?s)IsAllowed\(HavenBoardCommand command\).*?AddAttachmentCommand\s*=>\s*true') {
    throw 'Generative planner must not allow attachment mutation.'
}

$coordinatorText = Get-Content -LiteralPath $coordinator -Raw
if ($coordinatorText -match 'HttpClient|WebSocket|https?://') {
    throw 'Generative coordinator must not contain model/network provider behavior.'
}
if ($coordinatorText -notmatch 'MaxPreparedPlans\s*=\s*128' -or
    $coordinatorText -notmatch 'MaxUndoCheckpoints\s*=\s*128') {
    throw 'Generative coordinator must retain bounded prepared/undo registries.'
}
if ($coordinatorText -notmatch 'Dictionary<Guid, HavenBoardGenerativePlan> _prepared' -or
    $coordinatorText -notmatch 'Dictionary<Guid, HavenBoardAppliedGenerativePlan> _applied' -or
    $coordinatorText -notmatch 'ClonePlan\(' -or
    $coordinatorText -notmatch 'CloneApplied\(') {
    throw 'Generative coordinator must retain private detached prepared/applied state.'
}
if ($coordinatorText -notmatch 'ApplyAsync\(\s*HavenBoardsHuiSession session,\s*Guid planId' -or
    $coordinatorText -notmatch 'UndoAsync\(\s*HavenBoardsHuiSession session,\s*Guid planId') {
    throw 'Generative apply and undo must accept opaque plan IDs rather than caller-supplied payload/checkpoints.'
}
if ($coordinatorText -notmatch 'HavenBoardGenerativePlanner\.CreatePlan\(session\.Snapshot, plan\.Commands\)' -or
    $coordinatorText -notmatch 'session\.ExecuteBatchAsync\(' -or
    $coordinatorText -notmatch 'session\.RestoreSnapshotAsync\(') {
    throw 'Generative coordinator must revalidate plans and use the atomic session transaction/restore boundary.'
}

$sessionText = Get-Content -LiteralPath $session -Raw
if ($sessionText -notmatch 'ExecuteBatchAsync' -or
    $sessionText -notmatch 'RequireExpectedVersion' -or
    $sessionText -notmatch 'RestoreSnapshotAsync') {
    throw 'HUI session must retain optimistic batch and checkpoint-restore primitives.'
}
$saveIndex = $sessionText.IndexOf('await _store.SaveAsync(updated', [System.StringComparison]::Ordinal)
$publishIndex = $sessionText.IndexOf('PublishDurableSnapshot(updated', [System.StringComparison]::Ordinal)
if ($saveIndex -lt 0 -or $publishIndex -lt 0 -or $saveIndex -gt $publishIndex) {
    throw 'Generative batch path must persist once before publishing updated HUI state.'
}

$plannerTestsText = Get-Content -LiteralPath $plannerTests -Raw
if ($plannerTestsText -notmatch 'Planner_rejects_attachment_mutation_and_generated_field_abuse' -or
    $plannerTestsText -notmatch 'Planner_rejects_empty_and_oversized_batches' -or
    $plannerTestsText -notmatch 'Prepared_command_collection_cannot_be_replaced_after_review') {
    throw 'Neutral generative tests must cover allowlisting, bounds, and post-review command immutability.'
}

$coordinatorTestsText = Get-Content -LiteralPath $coordinatorTests -Raw
if ($coordinatorTestsText -notmatch 'Returned_preview_tampering_cannot_replace_coordinator_retained_plan' -or
    $coordinatorTestsText -notmatch 'Stale_plan_is_rejected_without_publishing_or_persisting_generated_state' -or
    $coordinatorTestsText -notmatch 'Invalid_batch_is_atomic_and_never_saves_partial_reduction' -or
    $coordinatorTestsText -notmatch 'Apply_is_one_shot_and_undo_uses_private_checkpoint_then_survives_reopen' -or
    $coordinatorTestsText -notmatch 'Undo_is_rejected_after_intervening_user_edit') {
    throw 'HUI generative tests must cover tampering, stale plans, atomic rollback, private undo, and stale undo.'
}

Write-Host 'Haven Boards generative UI safety checks passed.'
