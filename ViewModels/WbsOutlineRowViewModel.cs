using iscWBS.Core.Models;

namespace iscWBS.ViewModels;

/// <summary>Display data for a single row in the WBS outline list.</summary>
public sealed class WbsOutlineRowViewModel
{
    public WbsNode Node { get; init; } = null!;

    /// <summary>The effective status after applying auto-blocked derivation from the dependency graph.</summary>
    public WbsStatus EffectiveStatus { get; init; }

    // Forwarded for x:Bind — keeps the DataTemplate free of Node.* indirection
    public string    Code           => Node.Code;
    public string    Title          => Node.Title;
    public string    AssignedTo     => Node.AssignedTo;
    public double    EstimatedHours => Node.EstimatedHours;
    public double    ActualHours    => Node.ActualHours;
    public DateTime? DueDate        => Node.DueDate;
}
