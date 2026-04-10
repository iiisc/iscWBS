using SQLite;
using iscWBS.Core.Exceptions;
using iscWBS.Core.Models;
using iscWBS.Core.Services;

namespace iscWBS.Core.Repositories;

/// <summary>Data access for <see cref="NodeDependency"/> records.</summary>
public sealed class NodeDependencyRepository
{
    private SQLiteAsyncConnection Connection =>
        _projectStateService.Database
            ?? throw new WbsException("No project is currently open.");

    private readonly IProjectStateService _projectStateService;

    public NodeDependencyRepository(IProjectStateService projectStateService)
    {
        _projectStateService = projectStateService;
    }

    /// <summary>Returns all dependencies where the given node is the successor (its predecessors).</summary>
    public async Task<IReadOnlyList<NodeDependency>> GetBySuccessorAsync(Guid successorId)
        => await Connection.Table<NodeDependency>()
            .Where(d => d.SuccessorId == successorId)
            .ToListAsync();

    /// <summary>Returns all dependencies for nodes that belong to the given project.</summary>
    public async Task<IReadOnlyList<NodeDependency>> GetAllByProjectAsync(Guid projectId)
        => await Connection.QueryAsync<NodeDependency>(
            "SELECT d.Id, d.PredecessorId, d.SuccessorId, d.Type " +
            "FROM NodeDependencies d " +
            "INNER JOIN WbsNodes n ON d.SuccessorId = n.Id " +
            "WHERE n.ProjectId = ?",
            projectId.ToString());

    /// <summary>Inserts a new dependency record.</summary>
    public async Task InsertAsync(NodeDependency dependency)
        => await Connection.InsertAsync(dependency);

    /// <summary>Deletes a dependency by its primary key.</summary>
    public async Task DeleteAsync(int id)
        => await Connection.DeleteAsync<NodeDependency>(id);

    /// <summary>
    /// Deletes all dependencies where the given node appears as either
    /// predecessor or successor. Called when a <see cref="WbsNode"/> is deleted.
    /// </summary>
    public async Task DeleteByNodeAsync(Guid nodeId)
        => await Connection.ExecuteAsync(
            "DELETE FROM NodeDependencies WHERE PredecessorId = ? OR SuccessorId = ?",
            nodeId.ToString(), nodeId.ToString());
}
