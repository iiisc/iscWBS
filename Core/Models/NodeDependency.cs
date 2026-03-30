using SQLite;

namespace iscWBS.Core.Models;

[Table("NodeDependencies")]
public class NodeDependency
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public Guid PredecessorId { get; set; }

    [Indexed]
    public Guid SuccessorId { get; set; }

    public DependencyType Type { get; set; } = DependencyType.FinishToStart;
}
