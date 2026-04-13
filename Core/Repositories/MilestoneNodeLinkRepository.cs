using SQLite;
using iscWBS.Core.Exceptions;
using iscWBS.Core.Models;
using iscWBS.Core.Services;

namespace iscWBS.Core.Repositories;

/// <summary>Data access for <see cref="MilestoneNodeLink"/> records.</summary>
public sealed class MilestoneNodeLinkRepository
{
    private SQLiteAsyncConnection Connection =>
        _projectStateService.Database
            ?? throw new WbsException("No project is currently open.");

    private readonly IProjectStateService _projectStateService;

    public MilestoneNodeLinkRepository(IProjectStateService projectStateService)
        => _projectStateService = projectStateService;

    /// <summary>Returns all links for the given milestone.</summary>
    public async Task<IReadOnlyList<MilestoneNodeLink>> GetByMilestoneAsync(Guid milestoneId)
        => await Connection.Table<MilestoneNodeLink>()
            .Where(l => l.MilestoneId == milestoneId)
            .ToListAsync();

    /// <summary>
    /// Returns a dictionary mapping milestone ID to linked-node count for all milestones in the
    /// given project. Milestones with zero links are absent from the result.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, int>> GetCountsByProjectAsync(Guid projectId)
    {
        List<LinkCountRow> rows = await Connection.QueryAsync<LinkCountRow>(
            "SELECT l.MilestoneId, COUNT(l.Id) AS Count " +
            "FROM MilestoneNodeLinks l " +
            "INNER JOIN Milestones m ON l.MilestoneId = m.Id " +
            "WHERE m.ProjectId = ? " +
            "GROUP BY l.MilestoneId",
            projectId.ToString());

        return rows.ToDictionary(r => r.MilestoneId, r => r.Count);
    }

    /// <summary>Inserts a new link record.</summary>
    public async Task InsertAsync(MilestoneNodeLink link)
        => await Connection.InsertAsync(link);

    /// <summary>Deletes a link by its primary key.</summary>
    public async Task DeleteAsync(int id)
        => await Connection.DeleteAsync<MilestoneNodeLink>(id);

    /// <summary>Deletes all links for a milestone. Called before a milestone is deleted.</summary>
    public async Task DeleteByMilestoneAsync(Guid milestoneId)
        => await Connection.ExecuteAsync(
            "DELETE FROM MilestoneNodeLinks WHERE MilestoneId = ?",
            milestoneId.ToString());

    /// <summary>Deletes all links referencing a node. Called before a WBS node is deleted.</summary>
    public async Task DeleteByNodeAsync(Guid nodeId)
        => await Connection.ExecuteAsync(
            "DELETE FROM MilestoneNodeLinks WHERE NodeId = ?",
            nodeId.ToString());

    /// <summary>Returns the IDs of all milestones that link to the given node.</summary>
    public async Task<IReadOnlyList<Guid>> GetMilestoneIdsByNodeAsync(Guid nodeId)
    {
        List<MilestoneNodeLink> links = await Connection.Table<MilestoneNodeLink>()
            .Where(l => l.NodeId == nodeId)
            .ToListAsync();
        return links.Select(l => l.MilestoneId).ToList();
    }

    private class LinkCountRow
    {
        public Guid MilestoneId { get; set; }
        public int Count { get; set; }
    }
}
