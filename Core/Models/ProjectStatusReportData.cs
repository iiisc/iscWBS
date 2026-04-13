namespace iscWBS.Core.Models;

/// <summary>Immutable data snapshot used to compose the QuestPDF project status report.</summary>
public sealed record ProjectStatusReportData(
    string ProjectName,
    DateTime GeneratedAt,
    double OverallPercent,
    int TotalNodes,
    int CompleteCount,
    int InProgressCount,
    int AtRiskCount,
    int OverdueCount,
    int BlockedCount,
    IReadOnlyList<DeliverableRow> Deliverables,
    IReadOnlyList<(string Title, DateTime DueDate)> Milestones,
    IReadOnlyList<WbsTreeEntry> WbsTree);
