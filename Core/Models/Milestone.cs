namespace iscWBS.Core.Models;

public class Milestone
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
    public bool IsComplete { get; set; }

    /// <summary>How far ahead the dashboard and the report look for milestones.</summary>
    public const int UpcomingDays = 60;

    /// <summary>Still open and due within <see cref="UpcomingDays"/> of <paramref name="today"/>, overdue included.</summary>
    public bool IsDueSoon(DateTime today) => !IsComplete && DueDate.Date <= today.AddDays(UpcomingDays);
}
