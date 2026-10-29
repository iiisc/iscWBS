using iscWBS.Core.Models;

namespace iscWBS.ViewModels;

/// <summary>Display data for a single row in the WBS outline list.</summary>
public sealed class WbsOutlineRowViewModel
{
    public WbsNode Node { get; init; } = null!;

    public WbsStatus EffectiveStatus { get; init; }

    /// <summary>Brand colour level of the node's branch, 1–6.</summary>
    public int Level { get; init; } = 1;

    // Forwarded for x:Bind — keeps the DataTemplate free of Node.* indirection
    public string    Code           => Node.Code;
    public string    Title          => Node.Title;
    public string    AssignedTo     => Node.AssignedTo;
    public double    EstimatedHours => Node.EstimatedHours;
    public double    ActualHours    => Node.ActualHours;
    public string    DueDateText    => Node.DueDate?.ToString("d MMM yyyy") ?? string.Empty;
}
