using iscWBS.Core.Models;
using iscWBS.Helpers;

namespace iscWBS.ViewModels;

public sealed class MilestoneRowViewModel
{
    public Milestone Milestone { get; init; } = null!;
    public string DueDateLabel => Milestone.DueDate.ToString("d MMM yyyy");
    public int LinkedNodeCount { get; init; }

    public bool IsOverdue => !Milestone.IsComplete && Milestone.DueDate.Date < DateTime.Today;

    public bool IsUpcoming => !Milestone.IsComplete && !IsOverdue;

    public string LinkedNodeCountLabel => LinkedNodeCount switch
    {
        0 => Strings.Get("Milestones_NoNodes"),
        1 => Strings.Get("Milestones_OneNode"),
        _ => Strings.Format("Milestones_NodeCount", LinkedNodeCount)
    };

    public override string ToString() => $"{Milestone.Title}, {DueDateLabel}";
}
