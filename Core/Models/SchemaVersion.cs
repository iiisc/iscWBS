using SQLite;

namespace iscWBS.Core.Models;

[Table("SchemaVersion")]
public class SchemaVersion
{
    [PrimaryKey]
    public int Version { get; set; }

    public string AppliedAt { get; set; } = DateTime.UtcNow.ToString("o");
}
