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

    /// <summary>Nodes that cannot start yet: not active or done, with an unmet FS or SS predecessor.</summary>
    public static IReadOnlySet<Guid> BlockedNodeIds(
        IReadOnlyList<WbsNode> nodes, IReadOnlyList<NodeDependency> dependencies)
        => CollectSuccessors(nodes, dependencies, (succ, type, pred) =>
            succ is not (WbsStatus.Complete or WbsStatus.InProgress) && IsStartViolated(type, pred));

    /// <summary>
    /// Nodes that can start but not complete because of an unmet FF or SF predecessor.
    /// Start-blocked nodes are left out; they already carry the more severe indicator.
    /// </summary>
    public static IReadOnlySet<Guid> CompletionBlockedNodeIds(
        IReadOnlyList<WbsNode> nodes, IReadOnlyList<NodeDependency> dependencies)
    {
        HashSet<Guid> completionBlocked = CollectSuccessors(nodes, dependencies, (succ, type, pred) =>
            succ != WbsStatus.Complete && IsFinishViolated(type, pred));
        completionBlocked.ExceptWith(BlockedNodeIds(nodes, dependencies));
        return completionBlocked;
    }

    /// <summary>The status to show for <paramref name="node"/>: Blocked when it is in <paramref name="blockedNodeIds"/>.</summary>
    public static WbsStatus EffectiveStatus(WbsNode node, IReadOnlySet<Guid> blockedNodeIds)
        => blockedNodeIds.Contains(node.Id) ? WbsStatus.Blocked : node.Status;

    /// <summary>Complete nodes whose predecessor has since moved back into a state that violates the dependency.</summary>
    public static IReadOnlySet<Guid> ViolatedCompleteNodeIds(
        IReadOnlyList<WbsNode> nodes, IReadOnlyList<NodeDependency> dependencies)
        => CollectSuccessors(nodes, dependencies, (succ, type, pred) =>
            succ == WbsStatus.Complete && IsViolated(type, pred));

    /// <summary>In-progress nodes whose FS or SS predecessor has since moved back into a state that would block starting.</summary>
    public static IReadOnlySet<Guid> ViolatedInProgressNodeIds(
        IReadOnlyList<WbsNode> nodes, IReadOnlyList<NodeDependency> dependencies)
        => CollectSuccessors(nodes, dependencies, (succ, type, pred) =>
            succ == WbsStatus.InProgress && IsStartViolated(type, pred));

    /// <summary>The dependencies that stop a node moving to <paramref name="targetStatus"/>; only InProgress and Complete can be blocked.</summary>
    public static IReadOnlyList<NodeDependency> TransitionBlockers(
        WbsStatus targetStatus,
        IEnumerable<NodeDependency> predecessorDependencies,
        IReadOnlyDictionary<Guid, WbsStatus> statusById)
        => predecessorDependencies
            .Where(dep => statusById.TryGetValue(dep.PredecessorId, out WbsStatus pred) && targetStatus switch
            {
                WbsStatus.InProgress => IsStartViolated(dep.Type, pred),
                WbsStatus.Complete   => IsViolated(dep.Type, pred),
                _                    => false,
            })
            .ToList();

    private static HashSet<Guid> CollectSuccessors(
        IReadOnlyList<WbsNode> nodes,
        IReadOnlyList<NodeDependency> dependencies,
        Func<WbsStatus, DependencyType, WbsStatus, bool> isMatch)
    {
        var statusById = nodes.ToDictionary(n => n.Id, n => n.Status);
        var result = new HashSet<Guid>();
        foreach (NodeDependency dep in dependencies)
        {
            if (statusById.TryGetValue(dep.SuccessorId, out WbsStatus succ)
                && statusById.TryGetValue(dep.PredecessorId, out WbsStatus pred)
                && isMatch(succ, dep.Type, pred))
                result.Add(dep.SuccessorId);
        }
        return result;
    }

    /// <summary>Short PDM code, e.g. "FS".</summary>
    public static string GetAbbreviation(DependencyType type) => type switch
    {
        DependencyType.FinishToStart  => "FS",
        DependencyType.StartToStart   => "SS",
        DependencyType.FinishToFinish => "FF",
        DependencyType.StartToFinish  => "SF",
        _                             => type.ToString(),
    };
}
