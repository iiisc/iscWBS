using SQLite;
using iscWBS.Core.Data;
using iscWBS.Core.Exceptions;
using iscWBS.Core.Models;

namespace iscWBS.Core.Services;

/// <summary>Reads and edits the WBS nodes and dependencies of the open project, keeping outline codes in step.</summary>
public sealed class WbsService
{
    private readonly ProjectStateService _projectStateService;

    public WbsService(ProjectStateService projectStateService)
    {
        _projectStateService = projectStateService;
    }

    private SQLiteAsyncConnection Db => _projectStateService.Connection;

    public async Task<WbsNode?> GetByIdAsync(Guid id)
        => await Db.Table<WbsNode>().Where(n => n.Id == id).FirstOrDefaultAsync();

    /// <summary>All nodes of the project, ordered by <c>Code</c>.</summary>
    public async Task<IReadOnlyList<WbsNode>> GetAllByProjectAsync(Guid projectId)
        => await Db.QueryAsync<WbsNode>(
            "SELECT * FROM WbsNodes WHERE ProjectId = ? ORDER BY Code",
            projectId.ToString());

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
            await Db.InsertAsync(node);
            foreach (WbsNode n in changed.Where(n => n != node))
                await Db.UpdateAsync(n);
        });
        return node;
    }

    /// <summary>
    /// Saves the user-editable fields only. Code, SortOrder and the tree links belong to inserts and deletes,
    /// and the caller's copy may predate the last renumber.
    /// </summary>
    public async Task UpdateNodeAsync(WbsNode node)
        => await Db.ExecuteAsync(
            "UPDATE WbsNodes SET Title = ?, Description = ?, AssignedTo = ?, Status = ?, EstimatedHours = ?, " +
            "ActualHours = ?, StartDate = ?, DueDate = ?, IsDeliverable = ? WHERE Id = ?",
            node.Title, node.Description, node.AssignedTo, node.Status, node.EstimatedHours,
            node.ActualHours, node.StartDate, node.DueDate, node.IsDeliverable, node.Id.ToString());

    /// <summary>Deletes the node with its whole subtree, their dependencies and milestone links, then renumbers.</summary>
    public async Task DeleteNodeAsync(Guid id)
    {
        WbsNode node = await GetByIdAsync(id)
            ?? throw WbsException.NodeNotFound(id);

        IReadOnlyList<WbsNode> nodes = await GetAllByProjectAsync(node.ProjectId);
        HashSet<Guid> subtreeIds = new WbsHierarchy(nodes).CollectSubtree(node).Select(n => n.Id).ToHashSet();
        string placeholders = string.Join(",", subtreeIds.Select(_ => "?"));
        object[] ids = subtreeIds.Select(subtreeId => (object)subtreeId.ToString()).ToArray();

        await Db.InTransactionAsync(async () =>
        {
            await Db.ExecuteAsync(
                $"DELETE FROM NodeDependencies WHERE PredecessorId IN ({placeholders}) OR SuccessorId IN ({placeholders})",
                [.. ids, .. ids]);
            await Db.ExecuteAsync($"DELETE FROM MilestoneNodeLinks WHERE NodeId IN ({placeholders})", ids);
            await Db.ExecuteAsync($"DELETE FROM WbsNodes WHERE Id IN ({placeholders})", ids);
            foreach (WbsNode n in Renumber(nodes.Where(other => !subtreeIds.Contains(other.Id)).ToList()))
                await Db.UpdateAsync(n);
        });
    }

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
        => await Db.QueryAsync<NodeDependency>(
            "SELECT d.Id, d.PredecessorId, d.SuccessorId, d.Type " +
            "FROM NodeDependencies d " +
            "INNER JOIN WbsNodes n ON d.SuccessorId = n.Id " +
            "WHERE n.ProjectId = ?",
            projectId.ToString());

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

        await Db.InsertAsync(dependency);
    }

    public async Task RemoveDependencyAsync(int id)
        => await Db.DeleteAsync<NodeDependency>(id);

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
}
