namespace iscWBS.Core.Models;

/// <summary>Controls which optional sections are rendered in the generated PDF status report.</summary>
public sealed class ReportOptions
{
    /// <summary>Include the Summary (KPI) section.</summary>
    public bool IncludeSummary { get; set; } = true;

    /// <summary>Include the Attention Required section (overdue and at-risk leaf tasks).</summary>
    public bool IncludeAttentionRequired { get; set; } = true;

    /// <summary>Include the Deliverables rollup table (one row per root WBS node).</summary>
    public bool IncludeDeliverables { get; set; } = true;

    /// <summary>Include the Effort Summary section (estimated vs. actual hours).</summary>
    public bool IncludeEffortSummary { get; set; } = true;

    /// <summary>Include the Upcoming Milestones section.</summary>
    public bool IncludeMilestones { get; set; } = true;

    /// <summary>Include the Work Breakdown Structure full outline on page 2.</summary>
    public bool IncludeWbsTree { get; set; } = true;

    /// <summary>Returns a <see cref="ReportOptions"/> with every section enabled.</summary>
    public static ReportOptions All => new();
}
