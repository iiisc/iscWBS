namespace iscWBS.Core.Models;

/// <summary>A single row in the WBS outline section of the PDF report, produced by DFS traversal.</summary>
public sealed record WbsTreeEntry(
    string Code,
    string Title,
    int Depth,
    bool IsParent,
    WbsStatus Status,
    double ProgressFraction,
    DateTime? DueDate,
    string AssignedTo,
    double EstHours,
    double ActHours)
{
    public string DueDateShort =>
        DueDate.HasValue ? DueDate.Value.ToString("d MMM yy") : "\u2014";

    public string AssignedToShort =>
        string.IsNullOrWhiteSpace(AssignedTo) ? "\u2014" : AssignedTo;

    public bool IsOverdue => DueDateRules.IsOverdue(DueDate, Status);

    public bool IsAtRisk => DueDateRules.IsAtRisk(DueDate, Status);

    /// <summary>0 when not overdue.</summary>
    public int DaysOverdue =>
        IsOverdue ? (DateTime.Today - DueDate!.Value.Date).Days : 0;

    /// <summary>0 unless at risk.</summary>
    public int DaysUntilDue =>
        IsAtRisk ? (DueDate!.Value.Date - DateTime.Today).Days : 0;
}
