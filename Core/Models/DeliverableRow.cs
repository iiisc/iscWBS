namespace iscWBS.Core.Models;

/// <summary>Pre-computed data for a single top-level WBS deliverable in the executive status report.</summary>
public sealed record DeliverableRow(
    string Code,
    string Title,
    double ProgressFraction,
    WbsStatus Status,
    DateTime? DueDate)
{
    public int ProgressPercent => (int)(ProgressFraction * 100);

    public string DueDateText => DueDate.HasValue ? DueDate.Value.ToString("d MMM yyyy") : "—";

    public string ProgressPercentText => $"{ProgressPercent}%";

    /// <summary>
    /// One row per deliverable node. Progress is the share of complete nodes in its subtree, restricted to
    /// <paramref name="countedIds"/> when given; the due date is always the latest in the full subtree.
    /// </summary>
    public static IReadOnlyList<DeliverableRow> Build(
        IReadOnlyList<WbsNode> allNodes,
        IReadOnlySet<Guid> blockedIds,
        IReadOnlySet<Guid>? countedIds = null)
    {
        WbsHierarchy hierarchy = new(allNodes);
        var rows = new List<DeliverableRow>();
        foreach (WbsNode root in allNodes.Where(n => n.IsDeliverable).OrderBy(n => n.SortOrder))
        {
            List<WbsNode> subtree = hierarchy.CollectSubtree(root);
            List<WbsNode> counted = countedIds is null ? subtree : subtree.Where(n => countedIds.Contains(n.Id)).ToList();
            int complete = counted.Count(n => n.Status == WbsStatus.Complete);
            double fraction = counted.Count > 0 ? (double)complete / counted.Count : 0;
            DateTime? latestDue = subtree.Max(n => n.DueDate);

            rows.Add(new DeliverableRow(
                root.Code, root.Title, fraction, DependencyConstraints.EffectiveStatus(root, blockedIds), latestDue));
        }
        return rows;
    }
}
