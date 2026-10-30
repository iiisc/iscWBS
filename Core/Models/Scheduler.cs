namespace iscWBS.Core.Models;

/// <summary>Date rules that follow from dependencies between dated work.</summary>
public static class Scheduler
{
    /// <summary>
    /// Moves the successors of <paramref name="changedIds"/>, and theirs in turn, later until every dependency holds,
    /// keeping each one's duration. Only work that hasn't started moves, and nothing moves earlier.
    /// Returns the nodes it moved, with their new dates set.
    /// </summary>
    public static IReadOnlyList<WbsNode> PushSuccessors(
        IReadOnlyList<WbsNode> nodes, IReadOnlyList<NodeDependency> dependencies, IEnumerable<Guid> changedIds)
    {
        Dictionary<Guid, WbsNode> byId = nodes.ToDictionary(n => n.Id);
        ILookup<Guid, NodeDependency> outgoing = dependencies.ToLookup(d => d.PredecessorId);
        var moved = new List<WbsNode>();
        var queue = new Queue<Guid>(changedIds);
        // Dependencies can't form a cycle, so this ends; the cap only guards against a damaged file.
        for (int steps = 0; queue.Count > 0 && steps < nodes.Count * nodes.Count + 1; steps++)
        {
            WbsNode predecessor = byId[queue.Dequeue()];
            foreach (NodeDependency dependency in outgoing[predecessor.Id])
            {
                if (!byId.TryGetValue(dependency.SuccessorId, out WbsNode? successor)
                    || successor.Status is WbsStatus.InProgress or WbsStatus.Complete
                    || successor.StartDate is not { } start || successor.DueDate is not { } due)
                    continue;
                TimeSpan shift = RequiredShift(dependency, predecessor, start, due);
                if (shift <= TimeSpan.Zero)
                    continue;
                successor.StartDate = start + shift;
                successor.DueDate = due + shift;
                if (!moved.Contains(successor))
                    moved.Add(successor);
                queue.Enqueue(successor.Id);
            }
        }
        return moved;
    }

    /// <summary>
    /// The work that sets the project's finish: the last-finishing tasks and, back from them, every predecessor
    /// whose dependency leaves no slack. Parent nodes are left out, since their dates span their children's.
    /// </summary>
    public static IReadOnlySet<Guid> CriticalPath(IReadOnlyList<WbsNode> nodes, IReadOnlyList<NodeDependency> dependencies)
    {
        HashSet<Guid> parents = nodes.Where(n => n.ParentId.HasValue).Select(n => n.ParentId!.Value).ToHashSet();
        Dictionary<Guid, WbsNode> dated = nodes
            .Where(n => n.StartDate.HasValue && n.DueDate.HasValue && !parents.Contains(n.Id))
            .ToDictionary(n => n.Id);
        var critical = new HashSet<Guid>();
        if (dated.Count == 0)
            return critical;

        DateTime finish = dated.Values.Max(n => n.DueDate!.Value);
        ILookup<Guid, NodeDependency> incoming = dependencies.ToLookup(d => d.SuccessorId);
        var stack = new Stack<WbsNode>(dated.Values.Where(n => n.DueDate == finish));
        while (stack.Count > 0)
        {
            WbsNode node = stack.Pop();
            if (!critical.Add(node.Id))
                continue;
            foreach (NodeDependency dependency in incoming[node.Id])
            {
                if (dated.TryGetValue(dependency.PredecessorId, out WbsNode? predecessor)
                    && RequiredShift(dependency, predecessor, node.StartDate!.Value, node.DueDate!.Value) == TimeSpan.Zero)
                    stack.Push(predecessor);
            }
        }
        return critical;
    }

    /// <summary>How much later the successor must move for the dependency to hold; zero or less when it already does.</summary>
    private static TimeSpan RequiredShift(NodeDependency dependency, WbsNode predecessor, DateTime successorStart, DateTime successorDue)
        => dependency.Type switch
        {
            DependencyType.StartToStart   => predecessor.StartDate - successorStart,
            DependencyType.FinishToFinish => predecessor.DueDate - successorDue,
            DependencyType.StartToFinish  => predecessor.StartDate - successorDue,
            _                             => predecessor.DueDate - successorStart,
        } + TimeSpan.FromDays(dependency.LagDays) ?? TimeSpan.MinValue;
}
