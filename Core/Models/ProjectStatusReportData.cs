namespace iscWBS.Core.Models;

/// <summary>Immutable data snapshot used to compose the QuestPDF project status report.</summary>
public sealed record ProjectStatusReportData(
    string ProjectName,
    DateTime GeneratedAt,
    ProjectSummary Summary,
    IReadOnlyList<ProgressRow> Deliverables,
    IReadOnlyList<(string Title, DateTime DueDate)> Milestones,
    IReadOnlyList<WbsTreeEntry> WbsTree,
    IReadOnlyList<(DateTime Date, string Node, string Text)> Notes);
