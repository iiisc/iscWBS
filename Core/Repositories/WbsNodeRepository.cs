using SQLite;
using iscWBS.Core.Exceptions;
using iscWBS.Core.Models;
using iscWBS.Core.Services;

namespace iscWBS.Core.Repositories;

public sealed class WbsNodeRepository
{
    private SQLiteAsyncConnection Connection =>
        _projectStateService.Database
            ?? throw new WbsException("No project is currently open.");

    private readonly IProjectStateService _projectStateService;

    public WbsNodeRepository(IProjectStateService projectStateService)
    {
        _projectStateService = projectStateService;
    }

    public async Task<WbsNode?> GetByIdAsync(Guid id)
        => await Connection.Table<WbsNode>()
            .Where(n => n.Id == id)
            .FirstOrDefaultAsync();

    public async Task<IReadOnlyList<WbsNode>> GetRootNodesAsync(Guid projectId)
        => await Connection.QueryAsync<WbsNode>(
            "SELECT * FROM WbsNodes WHERE ProjectId = ? AND ParentId IS NULL ORDER BY SortOrder",
            projectId.ToString());

    public async Task<IReadOnlyList<WbsNode>> GetChildrenAsync(Guid parentId)
        => await Connection.QueryAsync<WbsNode>(
            "SELECT * FROM WbsNodes WHERE ParentId = ? ORDER BY SortOrder",
            parentId.ToString());

    public async Task<bool> HasChildrenAsync(Guid nodeId)
        => await Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM WbsNodes WHERE ParentId = ?",
            nodeId.ToString()) > 0;

    public async Task<IReadOnlyList<WbsNode>> GetAllByProjectAsync(Guid projectId)
        => await Connection.QueryAsync<WbsNode>(
            "SELECT * FROM WbsNodes WHERE ProjectId = ? ORDER BY Code",
            projectId.ToString());

    public async Task InsertAsync(WbsNode node)
        => await Connection.InsertAsync(node);

    public async Task UpdateAsync(WbsNode node)
        => await Connection.UpdateAsync(node);

    public async Task DeleteAsync(Guid id)
        => await Connection.DeleteAsync<WbsNode>(id.ToString());
}
