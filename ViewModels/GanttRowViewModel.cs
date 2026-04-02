using iscWBS.Core.Models;

namespace iscWBS.ViewModels;

/// <summary>Display data for a single row in the Gantt canvas.</summary>
public sealed class GanttRowViewModel
{
    public string Label { get; init; } = string.Empty;
    public double RowTop { get; init; }
    public bool HasBar { get; init; }
    public double BarLeft { get; init; }
    public double BarWidth { get; init; }
    public WbsStatus Status { get; init; }
    public bool IsUnscheduled => !HasBar;
}
