using SQLite;

namespace iscWBS.Core.Models;

[Table("WbsNodes")]
public class WbsNode
{
    [PrimaryKey]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Indexed]
    public Guid ProjectId { get; set; }

    [Indexed]
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
}
