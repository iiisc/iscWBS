using Microsoft.Data.Sqlite;
using iscWBS.Core.Data;
using iscWBS.Core.Exceptions;
using iscWBS.Core.Models;

namespace iscWBS.Core.Services;

/// <summary>Reads and edits the WBS nodes and dependencies of the open project, keeping outline codes in step.</summary>
public sealed class WbsService
{
    private const string Columns =
        "Id, ProjectId, ParentId, Code, Title, Description, AssignedTo, Status, " +
        "EstimatedHours, ActualHours, StartDate, DueDate, SortOrder, IsDeliverable";

    private readonly ProjectStateService _projectStateService;

    public WbsService(ProjectStateService projectStateService)
    {
        _projectStateService = projectStateService;
    }

    private SqliteConnection Db => _projectStateService.Connection;

    public async Task<WbsNode?> GetByIdAsync(Guid id)
        => (await Db.QueryAsync($"SELECT {Columns} FROM WbsNodes WHERE Id = $1", Read, id)).FirstOrDefault();

    /// <summary>All nodes of the project, ordered by <c>Code</c>.</summary>
    public async Task<IReadOnlyList<WbsNode>> GetAllByProjectAsync(Guid projectId)
        => await Db.QueryAsync($"SELECT {Columns} FROM WbsNodes WHERE ProjectId = $1 ORDER BY Code", Read, projectId);

    public Task<WbsNode> AddRootNodeAsync(Guid projectId, string title)
        => InsertNodeAsync(projectId, null, null, title);

    public async Task<WbsNode> AddChildNodeAsync(Guid parentId, string title)
    {
        WbsNode parent = await GetByIdAsync(parentId)
            ?? throw WbsException.NodeNotFound(parentId);
        return await InsertNodeAsync(parent.ProjectId, parentId, null, title);
    }

    /// <summary>Inserts a node directly after <paramref name="siblingId"/>, shifting later siblings down.</summary>
    public async Task<WbsNode> AddSiblingNodeAsync(Guid siblingId, string title)
    {
        WbsNode sibling = await GetByIdAsync(siblingId)
            ?? throw WbsException.NodeNotFound(siblingId);
        return await InsertNodeAsync(sibling.ProjectId, sibling.ParentId, sibling.SortOrder + 1, title);
    }

    /// <summary>Inserts at position <paramref name="insertAt"/> among the parent's children, or last when null, then renumbers.</summary>
    private async Task<WbsNode> InsertNodeAsync(Guid projectId, Guid? parentId, int? insertAt, string title)
    {
        var node = new WbsNode
        {
            ProjectId = projectId,
            ParentId = parentId,
            Title = title,
            SortOrder = insertAt ?? int.MaxValue
        };
        await Db.InTransactionAsync(async () =>
        {
            // First in the list, so the stable sort puts it ahead of the sibling whose position it takes.
            List<WbsNode> nodes = [node, .. await GetAllByProjectAsync(projectId)];
            List<WbsNode> changed = Renumber(nodes);
            await Db.ExecuteAsync(
                $"INSERT INTO WbsNodes ({Columns}) VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14)",
                node.Id, node.ProjectId, node.ParentId, node.Code, node.Title, node.Description, node.AssignedTo, node.Status,
                node.EstimatedHours, node.ActualHours, node.StartDate, node.DueDate, node.SortOrder, node.IsDeliverable);
            foreach (WbsNode n in changed.Where(n => n != node))
                await SavePositionAsync(n);
        });
        return node;
    }

    /// <summary>
    /// Saves the user-editable fields only. Code, SortOrder and the tree links belong to inserts and deletes,
    /// and the caller's copy may predate the last renumber.
    /// </summary>
    public async Task UpdateNodeAsync(WbsNode node)
        => await Db.ExecuteAsync(
            "UPDATE WbsNodes SET Title = $1, Description = $2, AssignedTo = $3, Status = $4, EstimatedHours = $5, " +
            "ActualHours = $6, StartDate = $7, DueDate = $8, IsDeliverable = $9 WHERE Id = $10",
            node.Title, node.Description, node.AssignedTo, node.Status, node.EstimatedHours,
            node.ActualHours, node.StartDate, node.DueDate, node.IsDeliverable, node.Id);

    /// <summary>Deletes the node with its whole subtree, their dependencies and milestone links, then renumbers.</summary>
    public async Task DeleteNodeAsync(Guid id)
    {
        WbsNode node = await GetByIdAsync(id)
            ?? throw WbsException.NodeNotFound(id);

        IReadOnlyList<WbsNode> nodes = await GetAllByProjectAsync(node.ProjectId);
        HashSet<Guid> subtreeIds = new WbsHierarchy(nodes).CollectSubtree(node).Select(n => n.Id).ToHashSet();
        object?[] ids = subtreeIds.Select(subtreeId => (object?)subtreeId).ToArray();
        string placeholders = string.Join(", ", ids.Select((_, i) => $"${i + 1}"));

        await Db.InTransactionAsync(async () =>
        {
            await Db.ExecuteAsync(
                $"DELETE FROM NodeDependencies WHERE PredecessorId IN ({placeholders}) OR SuccessorId IN ({placeholders})", ids);
            await Db.ExecuteAsync($"DELETE FROM MilestoneNodeLinks WHERE NodeId IN ({placeholders})", ids);
            await Db.ExecuteAsync($"DELETE FROM WbsNodes WHERE Id IN ({placeholders})", ids);
            foreach (WbsNode n in Renumber(nodes.Where(other => !subtreeIds.Contains(other.Id)).ToList()))
                await SavePositionAsync(n);
        });
    }

    private Task SavePositionAsync(WbsNode node)
        => Db.ExecuteAsync("UPDATE WbsNodes SET Code = $1, SortOrder = $2 WHERE Id = $3", node.Code, node.SortOrder, node.Id);

    /// <summary>Rewrites <c>SortOrder</c> and the outline <c>Code</c> of every node from tree order; returns the nodes that changed.</summary>
    private static List<WbsNode> Renumber(IReadOnlyList<WbsNode> nodes)
    {
        var hierarchy = new WbsHierarchy(nodes);
        var changed = new List<WbsNode>();

        void Walk(IReadOnlyList<WbsNode> siblings, string parentCode)
        {
            for (int i = 0; i < siblings.Count; i++)
            {
                WbsNode node = siblings[i];
                string code = parentCode.Length == 0 ? $"{i + 1}" : $"{parentCode}.{i + 1}";
                if (node.Code != code || node.SortOrder != i)
                {
                    node.Code = code;
                    node.SortOrder = i;
                    changed.Add(node);
                }
                Walk(hierarchy.ChildrenOf(node.Id), code);
            }
        }

        Walk(nodes.Where(n => n.ParentId is null).OrderBy(n => n.SortOrder).ToList(), string.Empty);
        return changed;
    }

    public async Task<IReadOnlyList<NodeDependency>> GetAllDependenciesByProjectAsync(Guid projectId)
        => await Db.QueryAsync(
            "SELECT d.Id, d.PredecessorId, d.SuccessorId, d.Type FROM NodeDependencies d " +
            "INNER JOIN WbsNodes n ON d.SuccessorId = n.Id WHERE n.ProjectId = $1",
            r => new NodeDependency
            {
                Id = r.GetInt32(0),
                PredecessorId = r.GetGuidText(1),
                SuccessorId = r.GetGuidText(2),
                Type = (DependencyType)r.GetInt32(3)
            },
            projectId);

    public async Task AddDependencyAsync(NodeDependency dependency)
    {
        if (dependency.PredecessorId == dependency.SuccessorId)
            throw new WbsException("A node cannot depend on itself.").WithMessageKey("Error_SelfDependency");

        WbsNode successor = await GetByIdAsync(dependency.SuccessorId)
            ?? throw WbsException.NodeNotFound(dependency.SuccessorId);

        IReadOnlyList<NodeDependency> existing = await GetAllDependenciesByProjectAsync(successor.ProjectId);

        if (existing.Any(d => d.PredecessorId == dependency.PredecessorId
                           && d.SuccessorId   == dependency.SuccessorId))
            throw new WbsException("This dependency already exists.").WithMessageKey("Error_DuplicateDependency");

        if (WouldCreateCycle(dependency.PredecessorId, dependency.SuccessorId, existing))
            throw new WbsException(
                "Adding this dependency would create a circular dependency.").WithMessageKey("Error_CircularDependency");

        await Db.ExecuteAsync(
            "INSERT INTO NodeDependencies (PredecessorId, SuccessorId, Type) VALUES ($1, $2, $3)",
            dependency.PredecessorId, dependency.SuccessorId, dependency.Type);
    }

    public async Task RemoveDependencyAsync(int id)
        => await Db.ExecuteAsync("DELETE FROM NodeDependencies WHERE Id = $1", id);

    /// <summary>Whether <paramref name="predecessorId"/> is already reachable from <paramref name="successorId"/>, so the new edge would close a loop.</summary>
    private static bool WouldCreateCycle(
        Guid predecessorId,
        Guid successorId,
        IReadOnlyList<NodeDependency> existing)
    {
        var adj = new Dictionary<Guid, List<Guid>>();
        foreach (NodeDependency dep in existing)
        {
            if (!adj.TryGetValue(dep.PredecessorId, out List<Guid>? list))
                adj[dep.PredecessorId] = list = [];
            list.Add(dep.SuccessorId);
        }

        var visited = new HashSet<Guid>();
        var stack   = new Stack<Guid>();
        stack.Push(successorId);

        while (stack.Count > 0)
        {
            Guid current = stack.Pop();
            if (current == predecessorId) return true;
            if (!visited.Add(current)) continue;
            if (adj.TryGetValue(current, out List<Guid>? neighbors))
                foreach (Guid neighbor in neighbors)
                    stack.Push(neighbor);
        }

        return false;
    }

    private static WbsNode Read(SqliteDataReader r) => new()
    {
        Id = r.GetGuidText(0),
        ProjectId = r.GetGuidText(1),
        ParentId = r.GetNullableGuid(2),
        Code = r.GetText(3),
        Title = r.GetText(4),
        Description = r.GetText(5),
        AssignedTo = r.GetText(6),
        Status = (WbsStatus)r.GetInt32(7),
        EstimatedHours = r.GetDouble(8),
        ActualHours = r.GetDouble(9),
        StartDate = r.GetNullableDateTime(10),
        DueDate = r.GetNullableDateTime(11),
        SortOrder = r.GetInt32(12),
        IsDeliverable = r.GetBoolean(13)
    };
}
