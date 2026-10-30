namespace iscWBS.Core.Models;

/// <summary>Controls which optional sections are rendered in the generated PDF status report.</summary>
public sealed class ReportOptions
{
    public bool IncludeSummary { get; set; } = true;
    public bool IncludeAttentionRequired { get; set; } = true;
    public bool IncludeDeliverables { get; set; } = true;
    public bool IncludeEffortSummary { get; set; } = true;
    public bool IncludeMilestones { get; set; } = true;
    public bool IncludeWbsTree { get; set; } = true;
    public bool IncludeNotes { get; set; } = true;
    public bool IncludeRisks { get; set; } = true;

    public static ReportOptions All => new();
}
