using iscWBS.Core.Models;

namespace iscWBS.Core.Models;

/// <summary>Display data for a single row in the Gantt canvas.</summary>
public sealed class GanttRow
{
    public Guid NodeId { get; init; }
    public string Label { get; init; } = string.Empty;
    public double RowTop { get; init; }
    public bool HasBar { get; init; }
    public double BarLeft { get; init; }
    public double BarWidth { get; init; }
    public WbsStatus Status { get; init; }
    public int Depth { get; init; }
    public bool IsParent { get; init; }
    public string AssignedTo { get; init; } = string.Empty;
    public double PercentComplete { get; init; }
    public bool IsUnscheduled => !HasBar;
}
