using SQLite;

namespace iscWBS.Core.Models;

[Table("Projects")]
public class Project
{
    [PrimaryKey]
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string Currency { get; set; } = "USD";
    public DateTime? StartDate { get; set; }
}
