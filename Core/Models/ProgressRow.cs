namespace iscWBS.Core.Models;

/// <summary>A WBS node with the progress and latest due date of its whole subtree, for the dashboard and the report.</summary>
public sealed record ProgressRow(
    string Code,
    string Title,
    double ProgressFraction,
    WbsStatus Status,
    DateTime? DueDate,
    int Depth = 0,
    bool IsDeliverable = false,
    DateTime? BaselineDue = null)
{
    /// <summary>Days the due date has moved past the baseline; 0 when it hasn't, or there is no baseline.</summary>
    public int DaysLate => DueDate is { } due && BaselineDue is { } planned ? Math.Max(0, (due.Date - planned.Date).Days) : 0;

    public bool IsLate => DaysLate > 0;

    public int ProgressPercent => (int)Math.Round(ProgressFraction * 100);

    public string DueDateText => DueDate.HasValue ? DueDate.Value.ToString("d MMM yyyy") : "—";

    public string ProgressPercentText => $"{ProgressPercent}%";

    public override string ToString() => $"{Code} {Title}, {ProgressPercentText}";

    /// <summary>One row per node that <paramref name="include"/> accepts, in outline order.</summary>
    public static IReadOnlyList<ProgressRow> Build(
        IReadOnlyList<WbsNode> allNodes,
        IReadOnlySet<Guid> blockedIds,
        Func<WbsNode, bool> include)
    {
        WbsHierarchy hierarchy = new(allNodes);
        return hierarchy.DepthFirst()
            .Where(entry => include(entry.Node))
            .Select(entry => new ProgressRow(
                entry.Node.Code,
                entry.Node.Title,
                hierarchy.Progress(entry.Node),
                DependencyConstraints.EffectiveStatus(entry.Node, blockedIds),
                hierarchy.CollectSubtree(entry.Node).Max(n => n.DueDate),
                entry.Depth,
                entry.Node.IsDeliverable,
                hierarchy.CollectSubtree(entry.Node).Max(n => n.BaselineDue)))
            .ToList();
    }
}
