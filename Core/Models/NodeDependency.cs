namespace iscWBS.Core.Models;

public class NodeDependency
{
    public int Id { get; set; }
    public Guid PredecessorId { get; set; }
    public Guid SuccessorId { get; set; }
    public DependencyType Type { get; set; } = DependencyType.FinishToStart;

    /// <summary>Calendar days the successor waits after the dependency is met; negative lets it overlap.</summary>
    public int LagDays { get; set; }
}
