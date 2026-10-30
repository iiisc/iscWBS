namespace iscWBS.Core.Models;

/// <summary>A WBS node with the progress and latest due date of its whole subtree, for the dashboard and the report.</summary>
public sealed record ProgressRow(
    string Code,
    string Title,
    double ProgressFraction,
    WbsStatus Status,
    DateTime? DueDate,
    int Depth = 0,
    bool IsDeliverable = false)
{
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
                entry.Node.IsDeliverable))
            .ToList();
    }
}
