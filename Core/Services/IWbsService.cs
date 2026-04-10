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

    /// <summary>Returns all dependencies for nodes belonging to the given project.</summary>
    Task<IReadOnlyList<NodeDependency>> GetAllDependenciesByProjectAsync(Guid projectId);

    /// <summary>
    /// Derives the set of node IDs that are <em>start-blocked</em>: a predecessor's FS or SS
    /// constraint has not been satisfied so the node cannot begin work.
    /// Only FS and SS are considered — FF and SF constrain finishing, not starting.
    /// Nodes already <see cref="WbsStatus.InProgress"/> or <see cref="WbsStatus.Complete"/>
    /// are never included.
    /// </summary>
    IReadOnlySet<Guid> ResolveBlockedNodeIds(
        IReadOnlyList<WbsNode> nodes,
        IReadOnlyList<NodeDependency> dependencies);

    /// <summary>
    /// Derives the set of node IDs that are <em>completion-blocked</em>: an FF or SF predecessor
    /// constraint prevents the node from being marked Complete, but the node can still be started
    /// (InProgress). Nodes already in the start-blocked set are excluded — the more severe
    /// constraint takes precedence for display purposes.
    /// </summary>
    IReadOnlySet<Guid> ResolveCompletionBlockedNodeIds(
        IReadOnlyList<WbsNode> nodes,
        IReadOnlyList<NodeDependency> dependencies);

    /// <summary>
    /// Returns the set of node IDs that are <see cref="WbsStatus.Complete"/> but have at least
    /// one predecessor whose current status retroactively violates a dependency constraint.
    /// These nodes represent completed work whose logical prerequisites are no longer satisfied
    /// — the application does not auto-revert them, but surfaces the inconsistency visually.
    /// </summary>
    IReadOnlySet<Guid> ResolveViolatedCompleteNodeIds(
        IReadOnlyList<WbsNode> nodes,
        IReadOnlyList<NodeDependency> dependencies);

    /// <summary>
    /// Returns the set of node IDs that are <see cref="WbsStatus.InProgress"/> but have at least
    /// one predecessor whose current status retroactively violates an FS or SS start constraint.
    /// The node started legitimately, but the predecessor has since reverted to a state that would
    /// have prevented it from starting — the application surfaces this inconsistency visually.
    /// </summary>
    IReadOnlySet<Guid> ResolveViolatedInProgressNodeIds(
        IReadOnlyList<WbsNode> nodes,
        IReadOnlyList<NodeDependency> dependencies);

    /// <summary>
    /// Computes KPI summary metrics from pre-loaded node and blocked-node data.
    /// Pure computation — no database access.
    /// </summary>
    ProjectSummary ComputeProjectSummary(
        IReadOnlyList<WbsNode> nodes,
        IReadOnlySet<Guid> blockedNodeIds);

    /// <summary>
    /// Returns the predecessor dependencies that prevent a node from transitioning to
    /// <paramref name="targetStatus"/>. Uses pre-loaded data; pure computation — no database access.
    /// Only <see cref="WbsStatus.InProgress"/> and <see cref="WbsStatus.Complete"/> can have blockers;
    /// any other target status returns an empty list.
    /// </summary>
    IReadOnlyList<NodeDependency> GetStatusTransitionBlockers(
        WbsStatus targetStatus,
        IReadOnlyList<NodeDependency> predecessorDependencies,
        IReadOnlyDictionary<Guid, WbsStatus> predecessorStatusMap);
}
