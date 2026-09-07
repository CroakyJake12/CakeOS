using CakeOS.Apps.Boards.Contract;

namespace CakeOS.Apps.Boards.Hui;

public sealed record HavenBoardAppliedGenerativePlan(
    Guid PlanId,
    HavenBoardSnapshot Before,
    HavenBoardSnapshot After);

/// <summary>
/// Safe application boundary for generative Boards mutations.
///
/// A model/provider may propose typed commands to <see cref="PreparePlan"/>, but it never receives
/// a storage/session reference. Prepared plans are retained privately by ID; apply ignores caller-
/// supplied payloads and executes only the exact registered commands after an optimistic version check.
/// </summary>
public sealed class HavenBoardGenerativeUiCoordinator
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, HavenBoardGenerativePlan> _prepared = [];

    public HavenBoardGenerativePlan PreparePlan(
        HavenBoardSnapshot snapshot,
        IEnumerable<HavenBoardCommand> proposedCommands)
    {
        var plan = HavenBoardGenerativePlanner.CreatePlan(snapshot, proposedCommands);
        lock (_gate)
            _prepared.Add(plan.Id, plan);
        return plan;
    }

    public bool CancelPlan(Guid planId)
    {
        lock (_gate)
            return _prepared.Remove(planId);
    }

    public async Task<HavenBoardAppliedGenerativePlan> ApplyAsync(
        HavenBoardsHuiSession session,
        Guid planId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        HavenBoardGenerativePlan plan;
        lock (_gate)
        {
            if (!_prepared.TryGetValue(planId, out plan!))
                throw new InvalidOperationException("The generative board plan is unknown, cancelled, or already applied.");
        }

        // Rebuild the preview from the current base snapshot using the privately registered command
        // payload. This re-runs every allowlist, field bound, hierarchy, and geometry validation before
        // the session gets a chance to persist anything.
        if (session.Snapshot.Version != plan.BaseVersion)
        {
            throw new InvalidOperationException(
                $"Board version changed from prepared {plan.BaseVersion} to {session.Snapshot.Version}; prepare a new plan.");
        }

        var verified = HavenBoardGenerativePlanner.CreatePlan(session.Snapshot, plan.Commands);
        if (!SnapshotsEquivalent(verified.Preview, plan.Preview))
            throw new InvalidOperationException("The prepared generative board preview no longer matches its commands.");

        var before = session.Snapshot;
        var after = await session.ExecuteBatchAsync(
            plan.Commands,
            plan.BaseVersion,
            cancellationToken).ConfigureAwait(false);

        lock (_gate)
            _prepared.Remove(planId);

        return new HavenBoardAppliedGenerativePlan(planId, before, after);
    }

    public Task<HavenBoardSnapshot> UndoAsync(
        HavenBoardsHuiSession session,
        HavenBoardAppliedGenerativePlan applied,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(applied);
        return session.RestoreSnapshotAsync(
            applied.Before,
            applied.After.Version,
            cancellationToken);
    }

    private static bool SnapshotsEquivalent(HavenBoardSnapshot left, HavenBoardSnapshot right)
    {
        if (!string.Equals(left.Id, right.Id, StringComparison.Ordinal)
            || !string.Equals(left.Title, right.Title, StringComparison.Ordinal)
            || left.Version != right.Version
            || left.Groups.Count != right.Groups.Count)
            return false;

        for (var groupIndex = 0; groupIndex < left.Groups.Count; groupIndex++)
        {
            var leftGroup = left.Groups[groupIndex];
            var rightGroup = right.Groups[groupIndex];
            if (!string.Equals(leftGroup.Id, rightGroup.Id, StringComparison.Ordinal)
                || !string.Equals(leftGroup.Title, rightGroup.Title, StringComparison.Ordinal)
                || leftGroup.Cards.Count != rightGroup.Cards.Count)
                return false;

            for (var cardIndex = 0; cardIndex < leftGroup.Cards.Count; cardIndex++)
            {
                var leftCard = leftGroup.Cards[cardIndex];
                var rightCard = rightGroup.Cards[cardIndex];
                if (!string.Equals(leftCard.Id, rightCard.Id, StringComparison.Ordinal)
                    || !string.Equals(leftCard.Title, rightCard.Title, StringComparison.Ordinal)
                    || !string.Equals(leftCard.ParentCardId, rightCard.ParentCardId, StringComparison.Ordinal))
                    return false;

                var leftAttachments = leftCard.Attachments ?? [];
                var rightAttachments = rightCard.Attachments ?? [];
                if (leftAttachments.Count != rightAttachments.Count)
                    return false;
                for (var attachmentIndex = 0; attachmentIndex < leftAttachments.Count; attachmentIndex++)
                {
                    if (leftAttachments[attachmentIndex] != rightAttachments[attachmentIndex])
                        return false;
                }
            }
        }

        var leftFreeform = left.Freeform?.Items ?? [];
        var rightFreeform = right.Freeform?.Items ?? [];
        if (leftFreeform.Count != rightFreeform.Count)
            return false;
        for (var index = 0; index < leftFreeform.Count; index++)
        {
            if (leftFreeform[index] != rightFreeform[index])
                return false;
        }

        return true;
    }
}
