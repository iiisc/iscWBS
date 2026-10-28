using SQLite;
using iscWBS.Core.Exceptions;
using iscWBS.Core.Models;

namespace iscWBS.Core.Services;

/// <summary>Reads and edits the open project's milestones and their links to WBS nodes.</summary>
public sealed class MilestoneService
{
    private readonly ProjectStateService _projectStateService;

    public MilestoneService(ProjectStateService projectStateService)
    {
        _projectStateService = projectStateService;
    }

    private SQLiteAsyncConnection Db => _projectStateService.Connection;

    public async Task<IReadOnlyList<Milestone>> GetByProjectAsync(Guid projectId)
        => await Db.QueryAsync<Milestone>(
            "SELECT * FROM Milestones WHERE ProjectId = ? ORDER BY DueDate",
            projectId.ToString());

    /// <summary>Incomplete milestones due within 60 days from today, overdue ones included.</summary>
    public async Task<IReadOnlyList<Milestone>> GetUpcomingAsync(Guid projectId)
        => await Db.QueryAsync<Milestone>(
            "SELECT * FROM Milestones WHERE ProjectId = ? AND IsComplete = 0 AND DueDate <= ? ORDER BY DueDate",
            projectId.ToString(), DateTime.Today.AddDays(60));

    public async Task<Milestone> CreateAsync(Guid projectId, string title, DateTime dueDate)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new WbsException("Milestone title is required.").WithMessageKey("Error_MilestoneTitleRequired");

        var milestone = new Milestone
        {
            ProjectId = projectId,
            Title = title.Trim(),
            DueDate = dueDate
        };
        await Db.InsertAsync(milestone);
        return milestone;
    }

    public async Task UpdateAsync(Milestone milestone)
        => await Db.UpdateAsync(milestone);

    public async Task DeleteAsync(Guid id)
    {
        await Db.ExecuteAsync("DELETE FROM MilestoneNodeLinks WHERE MilestoneId = ?", id.ToString());
        await Db.DeleteAsync<Milestone>(id.ToString());
    }

    public async Task MarkCompleteAsync(Guid id)
    {
        Milestone milestone = await Db.Table<Milestone>().Where(m => m.Id == id).FirstOrDefaultAsync()
            ?? throw WbsException.NodeNotFound(id);
        milestone.IsComplete = true;
        await Db.UpdateAsync(milestone);
    }

    public async Task<IReadOnlyList<Guid>> GetLinkedNodeIdsAsync(Guid milestoneId)
        => (await GetLinksAsync(milestoneId)).Select(l => l.NodeId).ToList();

    public async Task<IReadOnlyList<Milestone>> GetByNodeAsync(Guid nodeId)
        => await Db.QueryAsync<Milestone>(
            "SELECT m.* FROM Milestones m INNER JOIN MilestoneNodeLinks l ON l.MilestoneId = m.Id " +
            "WHERE l.NodeId = ? ORDER BY m.DueDate",
            nodeId.ToString());

    /// <summary>Linked-node count per milestone; milestones without links are absent.</summary>
    public async Task<IReadOnlyDictionary<Guid, int>> GetLinkedCountsByProjectAsync(Guid projectId)
    {
        List<LinkCountRow> rows = await Db.QueryAsync<LinkCountRow>(
            "SELECT l.MilestoneId, COUNT(l.Id) AS Count " +
            "FROM MilestoneNodeLinks l " +
            "INNER JOIN Milestones m ON l.MilestoneId = m.Id " +
            "WHERE m.ProjectId = ? " +
            "GROUP BY l.MilestoneId",
            projectId.ToString());

        return rows.ToDictionary(r => r.MilestoneId, r => r.Count);
    }

    /// <summary>Links a node to a milestone; linking it twice is a no-op.</summary>
    public async Task LinkNodeAsync(Guid milestoneId, Guid nodeId)
    {
        if ((await GetLinksAsync(milestoneId)).Any(l => l.NodeId == nodeId))
            return;

        await Db.InsertAsync(new MilestoneNodeLink
        {
            MilestoneId = milestoneId,
            NodeId = nodeId
        });
    }

    public async Task UnlinkNodeAsync(Guid milestoneId, Guid nodeId)
    {
        MilestoneNodeLink? link = (await GetLinksAsync(milestoneId)).FirstOrDefault(l => l.NodeId == nodeId);
        if (link is not null)
            await Db.DeleteAsync<MilestoneNodeLink>(link.Id);
    }

    private Task<List<MilestoneNodeLink>> GetLinksAsync(Guid milestoneId)
        => Db.Table<MilestoneNodeLink>().Where(l => l.MilestoneId == milestoneId).ToListAsync();

    private sealed class LinkCountRow
    {
        public Guid MilestoneId { get; set; }
        public int Count { get; set; }
    }
}
