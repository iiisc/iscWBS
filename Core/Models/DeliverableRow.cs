namespace iscWBS.Core.Models;

/// <summary>Pre-computed data for a single top-level WBS deliverable in the executive status report.</summary>
public sealed record DeliverableRow(
    string Code,
    string Title,
    int TotalNodes,
    int CompleteNodes,
    double ProgressFraction,
    WbsStatus Status,
    DateTime? DueDate,
    bool IsOverdue,
    bool IsAtRisk)
{
    /// <summary>Progress expressed as a whole-number percentage (0–100).</summary>
    public int ProgressPercent => (int)(ProgressFraction * 100);

    /// <summary>Formatted due date, or an em-dash when no due date is set.</summary>
    public string DueDateText => DueDate.HasValue ? DueDate.Value.ToString("d MMM yyyy") : "\u2014";

    /// <summary>Progress percentage as a display string, e.g. "42%".</summary>
    public string ProgressPercentText => $"{ProgressPercent}%";
}
