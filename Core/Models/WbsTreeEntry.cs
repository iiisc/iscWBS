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
    /// <summary>Due date formatted as "d MMM yy", or an em-dash when absent.</summary>
    public string DueDateShort =>
        DueDate.HasValue ? DueDate.Value.ToString("d MMM yy") : "\u2014";

    /// <summary>Assignee name, or an em-dash when unassigned.</summary>
    public string AssignedToShort =>
        string.IsNullOrWhiteSpace(AssignedTo) ? "\u2014" : AssignedTo;

    /// <summary>True when this task is past its due date and not yet complete.</summary>
    public bool IsOverdue =>
        Status != WbsStatus.Complete &&
        DueDate.HasValue &&
        DueDate.Value.Date < DateTime.Today;

    /// <summary>True when this task is due within the next 14 days and not yet complete.</summary>
    public bool IsAtRisk =>
        Status != WbsStatus.Complete &&
        DueDate.HasValue &&
        DueDate.Value.Date >= DateTime.Today &&
        DueDate.Value.Date <= DateTime.Today.AddDays(14);

    /// <summary>Number of days past the due date; 0 when not overdue.</summary>
    public int DaysOverdue =>
        IsOverdue ? (DateTime.Today - DueDate!.Value.Date).Days : 0;

    /// <summary>Number of days until the due date; 0 when overdue or no due date.</summary>
    public int DaysUntilDue =>
        IsAtRisk ? (DueDate!.Value.Date - DateTime.Today).Days : 0;
}
