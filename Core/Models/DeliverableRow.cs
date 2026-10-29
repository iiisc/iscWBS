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
    /// One row per deliverable node, leaving out those with nothing in <paramref name="matchingIds"/> when given.
    /// Progress is the share of complete nodes in its subtree; the due date is the latest in the subtree.
    /// </summary>
    public static IReadOnlyList<DeliverableRow> Build(
        IReadOnlyList<WbsNode> allNodes,
        IReadOnlySet<Guid> blockedIds,
        IReadOnlySet<Guid>? matchingIds = null)
    {
        WbsHierarchy hierarchy = new(allNodes);
        var rows = new List<DeliverableRow>();
        foreach (WbsNode root in allNodes.Where(n => n.IsDeliverable).OrderBy(n => n.SortOrder))
        {
            List<WbsNode> subtree = hierarchy.CollectSubtree(root);
            if (matchingIds is not null && !subtree.Any(n => matchingIds.Contains(n.Id))) continue;
            double fraction = (double)subtree.Count(n => n.Status == WbsStatus.Complete) / subtree.Count;
            DateTime? latestDue = subtree.Max(n => n.DueDate);

            rows.Add(new DeliverableRow(
                root.Code, root.Title, fraction, DependencyConstraints.EffectiveStatus(root, blockedIds), latestDue));
        }
        return rows;
    }
}
