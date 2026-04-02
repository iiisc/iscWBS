using iscWBS.Core.Models;

namespace iscWBS.ViewModels;

/// <summary>Display data for a single milestone row.</summary>
public sealed class MilestoneRowViewModel
{
    public Milestone Milestone { get; init; } = null!;
    public string DueDateLabel { get; init; } = string.Empty;
    public int LinkedNodeCount { get; init; }

    public bool IsOverdue => !Milestone.IsComplete &&
        DateTime.SpecifyKind(Milestone.DueDate, DateTimeKind.Utc) < DateTime.UtcNow;

    public bool IsUpcoming => !Milestone.IsComplete &&
        DateTime.SpecifyKind(Milestone.DueDate, DateTimeKind.Utc) >= DateTime.UtcNow;

    public string LinkedNodeCountLabel => LinkedNodeCount switch
    {
        0 => "No nodes",
        1 => "1 node",
        _ => $"{LinkedNodeCount} nodes"
    };
}
