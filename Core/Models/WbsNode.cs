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
    /// <summary>The dates when the baseline was last saved; null before that, or when the node had none then.</summary>
    public DateTime? BaselineStart { get; set; }
    public DateTime? BaselineDue { get; set; }

    /// <summary>A copy of the planned work as not started: status, actual hours and the baseline aren't carried over.</summary>
    public WbsNode CopyAsNotStarted() => new()
    {
        ProjectId = ProjectId,
        ParentId = ParentId,
        Code = Code,
        Title = Title,
        Description = Description,
        AssignedTo = AssignedTo,
        EstimatedHours = EstimatedHours,
        StartDate = StartDate,
        DueDate = DueDate,
        SortOrder = SortOrder,
        IsDeliverable = IsDeliverable
    };

    // Screen readers announce list and picker items by their ToString.
    public override string ToString() => $"{Code} {Title}";
}
