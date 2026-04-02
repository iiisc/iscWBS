using iscWBS.Core.Models;

namespace iscWBS.Core.Services;

/// <summary>Business logic for WBS node tree operations.</summary>
public interface IWbsService
{
    /// <summary>Returns the node with the specified id, or <see langword="null"/> if not found.</summary>
    Task<WbsNode?> GetByIdAsync(Guid id);

    /// <summary>Returns all root nodes for a project, ordered by <c>SortOrder</c>.</summary>
    Task<IReadOnlyList<WbsNode>> GetRootNodesAsync(Guid projectId);

    /// <summary>Returns the direct children of a node, ordered by <c>SortOrder</c>.</summary>
    Task<IReadOnlyList<WbsNode>> GetChildrenAsync(Guid parentId);

    /// <summary>Returns <see langword="true"/> if the node has any children.</summary>
    Task<bool> HasChildrenAsync(Guid nodeId);

    /// <summary>Returns all nodes for a project ordered by <c>Code</c>.</summary>
    Task<IReadOnlyList<WbsNode>> GetAllByProjectAsync(Guid projectId);

    /// <summary>Adds a new root node to the project and recalculates WBS codes.</summary>
    Task<WbsNode> AddRootNodeAsync(Guid projectId, string title);

    /// <summary>Adds a new child node under the specified parent and recalculates WBS codes.</summary>
    Task<WbsNode> AddChildNodeAsync(Guid parentId, string title);

    /// <summary>Adds a new sibling node immediately after the specified node and recalculates WBS codes.</summary>
    Task<WbsNode> AddSiblingNodeAsync(Guid siblingId, string title);

    /// <summary>Persists changes to an existing node.</summary>
    Task UpdateNodeAsync(WbsNode node);

    /// <summary>Deletes a node and all its descendants recursively, then recalculates WBS codes.</summary>
    Task DeleteNodeAsync(Guid id);

    /// <summary>Moves a node to a new parent and sort position, then recalculates WBS codes.</summary>
    Task MoveNodeAsync(Guid nodeId, Guid? newParentId, int newSortOrder);

    /// <summary>Returns all predecessor dependencies for the given successor node.</summary>
    Task<IReadOnlyList<NodeDependency>> GetDependenciesAsync(Guid nodeId);

    /// <summary>Adds a new predecessor dependency for a node.</summary>
    Task AddDependencyAsync(NodeDependency dependency);

    /// <summary>Removes a dependency by its id.</summary>
    Task RemoveDependencyAsync(int id);
}
