using SQLite;

namespace iscWBS.Core.Models;

[Table("Milestones")]
public class Milestone
{
    [PrimaryKey]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Indexed]
    public Guid ProjectId { get; set; }

    public string Title { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
    public bool IsComplete { get; set; }
}
