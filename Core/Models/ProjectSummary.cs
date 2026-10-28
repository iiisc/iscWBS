namespace iscWBS.Core.Models;

/// <summary>KPI snapshot shared by the Dashboard and the PDF report, so both show the same numbers.</summary>
public sealed record ProjectSummary(
    int TotalNodes,
    int CompleteCount,
    int InProgressCount,
    int BlockedCount,
    int OverdueCount,
    int AtRiskCount,
    double PercentComplete)
{
    public static ProjectSummary Compute(IReadOnlyList<WbsNode> nodes, IReadOnlySet<Guid> blockedNodeIds)
    {
        int total    = nodes.Count;
        int complete = nodes.Count(n => n.Status == WbsStatus.Complete);
        return new ProjectSummary(
            total,
            complete,
            nodes.Count(n => n.Status == WbsStatus.InProgress),
            // Status == Blocked only exists in files from older versions; it can no longer be chosen in the editor.
            nodes.Count(n => blockedNodeIds.Contains(n.Id) || n.Status == WbsStatus.Blocked),
            nodes.Count(n => DueDateRules.IsOverdue(n.DueDate, n.Status)),
            nodes.Count(n => DueDateRules.IsAtRisk(n.DueDate, n.Status)),
            total == 0 ? 0 : Math.Round(complete * 100.0 / total, 1));
    }
}
