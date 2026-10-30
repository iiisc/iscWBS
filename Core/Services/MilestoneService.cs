using Microsoft.Data.Sqlite;
using iscWBS.Core.Data;
using iscWBS.Core.Exceptions;
using iscWBS.Core.Models;

namespace iscWBS.Core.Services;

/// <summary>Reads and edits the open project's milestones and their links to WBS nodes.</summary>
public sealed class MilestoneService
{
    private const string Columns = "m.Id, m.ProjectId, m.Title, m.DueDate, m.IsComplete";

    public const int UpcomingDays = 60;

    private readonly ProjectStateService _projectStateService;

    public MilestoneService(ProjectStateService projectStateService)
    {
        _projectStateService = projectStateService;
    }

    private SqliteConnection Db => _projectStateService.Connection;

    public async Task<IReadOnlyList<Milestone>> GetByProjectAsync(Guid projectId)
        => await Db.QueryAsync(
            $"SELECT {Columns} FROM Milestones m WHERE m.ProjectId = $1 ORDER BY m.DueDate",
            Read, projectId);

    /// <summary>Incomplete milestones due within <see cref="UpcomingDays"/> from today, overdue ones included.</summary>
    public async Task<IReadOnlyList<Milestone>> GetUpcomingAsync(Guid projectId)
        => await Db.QueryAsync(
            $"SELECT {Columns} FROM Milestones m WHERE m.ProjectId = $1 AND m.IsComplete = 0 AND m.DueDate <= $2 ORDER BY m.DueDate",
            Read, projectId, DateTime.Today.AddDays(UpcomingDays));

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
        await Db.ExecuteAsync(
            "INSERT INTO Milestones (Id, ProjectId, Title, DueDate, IsComplete) VALUES ($1, $2, $3, $4, $5)",
            milestone.Id, milestone.ProjectId, milestone.Title, milestone.DueDate, milestone.IsComplete);
        return milestone;
    }

    public async Task UpdateAsync(Milestone milestone)
        => await Db.ExecuteAsync(
            "UPDATE Milestones SET Title = $1, DueDate = $2, IsComplete = $3 WHERE Id = $4",
            milestone.Title, milestone.DueDate, milestone.IsComplete, milestone.Id);

    public async Task DeleteAsync(Guid id)
    {
        await Db.ExecuteAsync("DELETE FROM MilestoneNodeLinks WHERE MilestoneId = $1", id);
        await Db.ExecuteAsync("DELETE FROM Milestones WHERE Id = $1", id);
    }

    public async Task MarkCompleteAsync(Guid id)
    {
        if (await Db.ExecuteAsync("UPDATE Milestones SET IsComplete = 1 WHERE Id = $1", id) == 0)
            throw WbsException.MilestoneNotFound(id);
    }

    public async Task<IReadOnlyList<Guid>> GetLinkedNodeIdsAsync(Guid milestoneId)
        => await Db.QueryAsync(
            "SELECT NodeId FROM MilestoneNodeLinks WHERE MilestoneId = $1 ORDER BY Id",
            r => r.GetGuidText(0), milestoneId);

    public async Task<IReadOnlyList<Milestone>> GetByNodeAsync(Guid nodeId)
        => await Db.QueryAsync(
            $"SELECT {Columns} FROM Milestones m INNER JOIN MilestoneNodeLinks l ON l.MilestoneId = m.Id " +
            "WHERE l.NodeId = $1 ORDER BY m.DueDate",
            Read, nodeId);

    /// <summary>Linked-node count per milestone; milestones without links are absent.</summary>
    public async Task<IReadOnlyDictionary<Guid, int>> GetLinkedCountsByProjectAsync(Guid projectId)
        => (await Db.QueryAsync(
                "SELECT l.MilestoneId, COUNT(l.Id) FROM MilestoneNodeLinks l " +
                "INNER JOIN Milestones m ON l.MilestoneId = m.Id WHERE m.ProjectId = $1 GROUP BY l.MilestoneId",
                r => (MilestoneId: r.GetGuidText(0), Count: r.GetInt32(1)), projectId))
            .ToDictionary(r => r.MilestoneId, r => r.Count);

    /// <summary>Links a node to a milestone; linking it twice is a no-op.</summary>
    public async Task LinkNodeAsync(Guid milestoneId, Guid nodeId)
    {
        if ((await GetLinkedNodeIdsAsync(milestoneId)).Contains(nodeId))
            return;

        await Db.ExecuteAsync("INSERT INTO MilestoneNodeLinks (MilestoneId, NodeId) VALUES ($1, $2)", milestoneId, nodeId);
    }

    public async Task UnlinkNodeAsync(Guid milestoneId, Guid nodeId)
        => await Db.ExecuteAsync("DELETE FROM MilestoneNodeLinks WHERE MilestoneId = $1 AND NodeId = $2", milestoneId, nodeId);

    private static Milestone Read(SqliteDataReader r) => new()
    {
        Id = r.GetGuidText(0),
        ProjectId = r.GetGuidText(1),
        Title = r.GetText(2),
        DueDate = r.GetNullableDateTime(3) ?? default,
        IsComplete = r.GetBoolean(4)
    };
}
