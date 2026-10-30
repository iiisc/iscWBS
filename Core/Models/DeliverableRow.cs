namespace iscWBS.Core.Models;

/// <summary>Pre-computed data for a single top-level WBS deliverable in the executive status report.</summary>
public sealed record DeliverableRow(
    string Code,
    string Title,
    double ProgressFraction,
    WbsStatus Status,
    DateTime? DueDate)
{
    public int ProgressPercent => (int)Math.Round(ProgressFraction * 100);

    public string DueDateText => DueDate.HasValue ? DueDate.Value.ToString("d MMM yyyy") : "—";

    public string ProgressPercentText => $"{ProgressPercent}%";

    public override string ToString() => $"{Code} {Title}, {ProgressPercentText}";

    /// <summary>
    /// One row per deliverable node, leaving out those with nothing in <paramref name="matchingIds"/> when given.
    /// Rows are in outline order; progress and the due date cover the whole subtree.
    /// </summary>
    public static IReadOnlyList<DeliverableRow> Build(
        IReadOnlyList<WbsNode> allNodes,
        IReadOnlySet<Guid> blockedIds,
        IReadOnlySet<Guid>? matchingIds = null)
    {
        WbsHierarchy hierarchy = new(allNodes);
        var rows = new List<DeliverableRow>();
        foreach ((WbsNode root, _) in hierarchy.DepthFirst().Where(entry => entry.Node.IsDeliverable))
        {
            List<WbsNode> subtree = hierarchy.CollectSubtree(root);
            if (matchingIds is not null && !subtree.Any(n => matchingIds.Contains(n.Id))) continue;
            DateTime? latestDue = subtree.Max(n => n.DueDate);

            rows.Add(new DeliverableRow(
                root.Code, root.Title, hierarchy.Progress(root), DependencyConstraints.EffectiveStatus(root, blockedIds), latestDue));
        }
        return rows;
    }
}
