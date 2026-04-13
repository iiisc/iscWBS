using SQLite;

namespace iscWBS.Core.Models;

/// <summary>Junction record linking a <see cref="Milestone"/> to a <see cref="WbsNode"/>.</summary>
[Table("MilestoneNodeLinks")]
public class MilestoneNodeLink
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public Guid MilestoneId { get; set; }

    [Indexed]
    public Guid NodeId { get; set; }
}
