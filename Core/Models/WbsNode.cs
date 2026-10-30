namespace iscWBS.Core.Models;

public class WbsNode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Guid? ParentId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string AssignedTo { get; set; } = string.Empty;
    public WbsStatus Status { get; set; } = WbsStatus.NotStarted;
    public double EstimatedHours { get; set; }
    public double ActualHours { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? DueDate { get; set; }
    public int SortOrder { get; set; }
    public bool IsDeliverable { get; set; }

    // Screen readers announce list and picker items by their ToString.
    public override string ToString() => $"{Code} {Title}";
}
