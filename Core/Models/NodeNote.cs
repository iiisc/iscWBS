namespace iscWBS.Core.Models;

/// <summary>A dated note on a WBS node: what happened, or what was decided.</summary>
public sealed class NodeNote
{
    public int Id { get; set; }
    public Guid NodeId { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Text { get; set; } = string.Empty;
}
