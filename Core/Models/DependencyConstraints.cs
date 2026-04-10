namespace iscWBS.Core.Models;

/// <summary>
/// Pure domain predicates for the four PDM dependency types.
/// All methods are stateless and side-effect free — this is the single
/// source of truth for what each dependency type requires of its predecessor.
/// </summary>
public static class DependencyConstraints
{
    /// <summary>
    /// Returns <see langword="true"/> when an FS or SS constraint is violated and the successor
    /// cannot yet <em>start</em> (i.e. be marked InProgress or Complete).
    /// </summary>
    public static bool IsStartViolated(DependencyType type, WbsStatus predecessorStatus) =>
        type switch
        {
            DependencyType.FinishToStart => predecessorStatus != WbsStatus.Complete,
            DependencyType.StartToStart  => predecessorStatus is WbsStatus.NotStarted or WbsStatus.Blocked,
            _                            => false,
        };

    /// <summary>
    /// Returns <see langword="true"/> when an FF or SF constraint is violated and the successor
    /// cannot yet <em>finish</em> (i.e. be marked Complete).
    /// </summary>
    public static bool IsFinishViolated(DependencyType type, WbsStatus predecessorStatus) =>
        type switch
        {
            DependencyType.FinishToFinish => predecessorStatus != WbsStatus.Complete,
            DependencyType.StartToFinish  => predecessorStatus is WbsStatus.NotStarted or WbsStatus.Blocked,
            _                             => false,
        };

    /// <summary>
    /// Returns <see langword="true"/> when <em>any</em> constraint on the successor is violated
    /// for the given predecessor status, regardless of whether it blocks starting or finishing.
    /// </summary>
    public static bool IsViolated(DependencyType type, WbsStatus predecessorStatus) =>
        IsStartViolated(type, predecessorStatus) || IsFinishViolated(type, predecessorStatus);

    /// <summary>
    /// Returns <see langword="true"/> for FF and SF — dependency types whose constraint applies
    /// to the <em>finish</em> of the successor rather than its start.
    /// Used by rendering code to choose a visual style (e.g. dashed line).
    /// </summary>
    public static bool IsFinishConstraint(DependencyType type) =>
        type is DependencyType.FinishToFinish or DependencyType.StartToFinish;

    /// <summary>
    /// Returns a human-readable explanation of why a constraint is blocking, or
    /// <see cref="string.Empty"/> if the constraint is not currently violated.
    /// </summary>
    public static string GetBlockingReason(DependencyType type, WbsStatus predecessorStatus)
    {
        if (!IsViolated(type, predecessorStatus)) return string.Empty;
        string statusLabel = FormatStatus(predecessorStatus);
        return type switch
        {
            DependencyType.FinishToStart  => $"Must finish before this can start \u2014 currently {statusLabel}",
            DependencyType.StartToStart   => $"Must start before this can start \u2014 currently {statusLabel}",
            DependencyType.FinishToFinish => $"Must finish before this can finish \u2014 currently {statusLabel}",
            DependencyType.StartToFinish  => $"Must start before this can finish \u2014 currently {statusLabel}",
            _                             => string.Empty,
        };
    }

    private static string FormatStatus(WbsStatus status) => status switch
    {
        WbsStatus.NotStarted => "Not Started",
        WbsStatus.InProgress => "In Progress",
        WbsStatus.Complete   => "Complete",
        WbsStatus.Blocked    => "Blocked",
        _                    => status.ToString(),
    };
}
