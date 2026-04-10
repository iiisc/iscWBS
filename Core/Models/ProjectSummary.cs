namespace iscWBS.Core.Models;

/// <summary>Pre-computed KPI snapshot for a project's WBS node set.</summary>
public sealed record ProjectSummary(
    int TotalNodes,
    int CompleteCount,
    int InProgressCount,
    int NotStartedCount,
    int BlockedCount,
    int OverdueCount,
    double PercentComplete);
