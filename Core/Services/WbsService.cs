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
        "EstimatedHours, ActualHours, StartDate, DueDate, SortOrder, IsDeliverable, BaselineStart, BaselineDue";

    private readonly ProjectStateService _projectStateService;

    public WbsService(ProjectStateService projectStateService)
    {
        _projectStateService = projectStateService;
    }

    private SqliteConnection Db => _projectStateService.Connection;

    private ProjectHistory History => _projectStateService.History;

    public async Task<WbsNode?> GetByIdAsync(Guid id)
        => (await Db.QueryAsync($"SELECT {Columns} FROM WbsNodes WHERE Id = $1", Read, id)).FirstOrDefault();

    /// <summary>All nodes of the project, in outline order (1.9 before 1.10, which sorting the codes as text gets wrong).</summary>
    public async Task<IReadOnlyList<WbsNode>> GetAllByProjectAsync(Guid projectId)
        => new WbsHierarchy(await Db.QueryAsync($"SELECT {Columns} FROM WbsNodes WHERE ProjectId = $1", Read, projectId))
            .DepthFirst().Select(entry => entry.Node).ToList();

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
        await History.ChangeAsync(async () =>
        {
            // First in the list, so the stable sort puts it ahead of the sibling whose position it takes.
            List<WbsNode> nodes = [node, .. await GetAllByProjectAsync(projectId)];
            List<WbsNode> changed = Renumber(nodes);
            await Db.ExecuteAsync(
                $"INSERT INTO WbsNodes ({Columns}) VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, $16)",
                node.Id, node.ProjectId, node.ParentId, node.Code, node.Title, node.Description, node.AssignedTo, node.Status,
                node.EstimatedHours, node.ActualHours, node.StartDate, node.DueDate, node.SortOrder, node.IsDeliverable,
                node.BaselineStart, node.BaselineDue);
            foreach (WbsNode n in changed.Where(n => n != node))
                await SavePositionAsync(n);
        });
        return node;
    }

    /// <summary>
    /// Saves the user-editable fields only. Code, SortOrder and the tree links belong to inserts, moves and deletes,
    /// and the caller's copy may predate the last renumber. When the dates change, work that depends on the node
    /// moves later as far as its dependencies need; returns what moved.
    /// </summary>
    public async Task<IReadOnlyList<WbsNode>> UpdateNodeAsync(WbsNode node)
    {
        WbsNode? before = await GetByIdAsync(node.Id);
        IReadOnlyList<WbsNode> moved = [];
        await History.ChangeAsync(async () =>
        {
            await Db.ExecuteAsync(
                "UPDATE WbsNodes SET Title = $1, Description = $2, AssignedTo = $3, Status = $4, EstimatedHours = $5, " +
                "ActualHours = $6, StartDate = $7, DueDate = $8, IsDeliverable = $9 WHERE Id = $10",
                node.Title, node.Description, node.AssignedTo, node.Status, node.EstimatedHours,
                node.ActualHours, node.StartDate, node.DueDate, node.IsDeliverable, node.Id);
            if (before is not null && (before.StartDate != node.StartDate || before.DueDate != node.DueDate))
                moved = await PushSuccessorsAsync(before.ProjectId, node.Id);
        });
        return moved;
    }

    /// <summary>
    /// Moves <paramref name="id"/>'s branch to position <paramref name="index"/> among the children of
    /// <paramref name="newParentId"/> (the top level when null), then renumbers.
    /// </summary>
    public async Task MoveNodeAsync(Guid id, Guid? newParentId, int index)
    {
        WbsNode node = await GetByIdAsync(id)
            ?? throw WbsException.NodeNotFound(id);
        IReadOnlyList<WbsNode> nodes = await GetAllByProjectAsync(node.ProjectId);
        var hierarchy = new WbsHierarchy(nodes);
        WbsNode moving = nodes.First(n => n.Id == id);
        if (newParentId is { } target && hierarchy.CollectSubtree(moving).Any(n => n.Id == target))
            throw new WbsException("A node can't move into its own branch.").WithMessageKey("Error_MoveIntoOwnBranch");

        Dictionary<Guid, (string Code, int SortOrder)> original = nodes.ToDictionary(n => n.Id, n => (n.Code, n.SortOrder));
        List<WbsNode> siblings = (newParentId is { } parent ? hierarchy.ChildrenOf(parent) : hierarchy.Roots)
            .Where(n => n.Id != id)
            .ToList();
        siblings.Insert(Math.Clamp(index, 0, siblings.Count), moving);
        moving.ParentId = newParentId;
        for (int i = 0; i < siblings.Count; i++)
            siblings[i].SortOrder = i;
        Renumber(nodes);

        await History.ChangeAsync(async () =>
        {
            await Db.ExecuteAsync("UPDATE WbsNodes SET ParentId = $1 WHERE Id = $2", newParentId, id);
            foreach (WbsNode n in nodes.Where(n => original[n.Id] != (n.Code, n.SortOrder)))
                await SavePositionAsync(n);
        });
    }

    /// <summary>Saves every node's current dates as its baseline, to measure later changes against.</summary>
    public async Task SetBaselineAsync(Guid projectId)
    {
        await History.ChangeAsync(() => Db.ExecuteAsync("UPDATE WbsNodes SET BaselineStart = StartDate, BaselineDue = DueDate WHERE ProjectId = $1", projectId));
    }

    private async Task<IReadOnlyList<WbsNode>> PushSuccessorsAsync(Guid projectId, Guid changedId)
    {
        IReadOnlyList<WbsNode> moved = Scheduler.PushSuccessors(
            await GetAllByProjectAsync(projectId), await GetAllDependenciesByProjectAsync(projectId), [changedId]);
        foreach (WbsNode n in moved)
            await Db.ExecuteAsync("UPDATE WbsNodes SET StartDate = $1, DueDate = $2 WHERE Id = $3", n.StartDate, n.DueDate, n.Id);
        return moved;
    }

    /// <summary>Deletes the node with its whole subtree, their dependencies, milestone links and notes, then renumbers.</summary>
    public async Task DeleteNodeAsync(Guid id)
    {
        WbsNode node = await GetByIdAsync(id)
            ?? throw WbsException.NodeNotFound(id);

        IReadOnlyList<WbsNode> nodes = await GetAllByProjectAsync(node.ProjectId);
        HashSet<Guid> subtreeIds = new WbsHierarchy(nodes).CollectSubtree(node).Select(n => n.Id).ToHashSet();
        object?[] ids = subtreeIds.Select(subtreeId => (object?)subtreeId).ToArray();
        string placeholders = string.Join(", ", ids.Select((_, i) => $"${i + 1}"));

        await History.ChangeAsync(async () =>
        {
            await Db.ExecuteAsync(
                $"DELETE FROM NodeDependencies WHERE PredecessorId IN ({placeholders}) OR SuccessorId IN ({placeholders})", ids);
            await Db.ExecuteAsync($"DELETE FROM MilestoneNodeLinks WHERE NodeId IN ({placeholders})", ids);
            await Db.ExecuteAsync($"DELETE FROM NodeNotes WHERE NodeId IN ({placeholders})", ids);
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

        Walk(hierarchy.Roots, string.Empty);
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

    /// <summary>Adds the dependency and moves the successor's not-yet-started work later if it needs to; returns what moved.</summary>
    public async Task<IReadOnlyList<WbsNode>> AddDependencyAsync(NodeDependency dependency)
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

        IReadOnlyList<WbsNode> moved = [];
        await History.ChangeAsync(async () =>
        {
            await Db.ExecuteAsync(
                "INSERT INTO NodeDependencies (PredecessorId, SuccessorId, Type) VALUES ($1, $2, $3)",
                dependency.PredecessorId, dependency.SuccessorId, dependency.Type);
            moved = await PushSuccessorsAsync(successor.ProjectId, dependency.PredecessorId);
        });
        return moved;
    }

    public async Task RemoveDependencyAsync(int id)
    {
        await History.ChangeAsync(() => Db.ExecuteAsync("DELETE FROM NodeDependencies WHERE Id = $1", id));
    }

    /// <summary>The node's notes, newest first.</summary>
    public async Task<IReadOnlyList<NodeNote>> GetNotesAsync(Guid nodeId)
        => await Db.QueryAsync(
            "SELECT Id, NodeId, CreatedAt, Text FROM NodeNotes WHERE NodeId = $1 ORDER BY CreatedAt DESC, Id DESC",
            ReadNote, nodeId);

    /// <summary>Notes on the project's nodes written since <paramref name="since"/>, newest first, with the node each belongs to.</summary>
    public async Task<IReadOnlyList<(NodeNote Note, string Code, string Title)>> GetNotesSinceAsync(Guid projectId, DateTime since)
        => await Db.QueryAsync(
            "SELECT nn.Id, nn.NodeId, nn.CreatedAt, nn.Text, n.Code, n.Title FROM NodeNotes nn " +
            "INNER JOIN WbsNodes n ON nn.NodeId = n.Id WHERE n.ProjectId = $1 AND nn.CreatedAt >= $2 " +
            "ORDER BY nn.CreatedAt DESC, nn.Id DESC",
            r => (ReadNote(r), r.GetText(4), r.GetText(5)), projectId, since);

    public async Task<NodeNote> AddNoteAsync(Guid nodeId, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new WbsException("A note needs some text.").WithMessageKey("Error_NoteTextRequired");

        var note = new NodeNote { NodeId = nodeId, CreatedAt = DateTime.Now, Text = text.Trim() };
        await History.ChangeAsync(async () =>
        {
            await Db.ExecuteAsync("INSERT INTO NodeNotes (NodeId, CreatedAt, Text) VALUES ($1, $2, $3)", note.NodeId, note.CreatedAt, note.Text);
            note.Id = await Db.ScalarAsync<int>("SELECT last_insert_rowid()");
        });
        return note;
    }

    public async Task DeleteNoteAsync(int id)
    {
        await History.ChangeAsync(() => Db.ExecuteAsync("DELETE FROM NodeNotes WHERE Id = $1", id));
    }

    private static NodeNote ReadNote(SqliteDataReader r) => new()
    {
        Id = r.GetInt32(0),
        NodeId = r.GetGuidText(1),
        CreatedAt = r.GetNullableDateTime(2) ?? default,
        Text = r.GetText(3)
    };

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
        IsDeliverable = r.GetBoolean(13),
        BaselineStart = r.GetNullableDateTime(14),
        BaselineDue = r.GetNullableDateTime(15)
    };
}
