using SQLite;
using iscWBS.Core.Exceptions;
using iscWBS.Core.Models;
using iscWBS.Core.Services;

namespace iscWBS.Core.Repositories;

public sealed class MilestoneRepository
{
    private SQLiteAsyncConnection Connection =>
        _projectStateService.Database
            ?? throw new WbsException("No project is currently open.");

    private readonly IProjectStateService _projectStateService;

    public MilestoneRepository(IProjectStateService projectStateService)
        => _projectStateService = projectStateService;

    /// <summary>Returns all milestones for a project ordered by due date.</summary>
    public async Task<Milestone?> GetByIdAsync(Guid id)
        => await Connection.Table<Milestone>()
            .Where(m => m.Id == id)
            .FirstOrDefaultAsync();

    public async Task<IReadOnlyList<Milestone>> GetByProjectAsync(Guid projectId)
        => await Connection.QueryAsync<Milestone>(
            "SELECT * FROM Milestones WHERE ProjectId = ? ORDER BY DueDate",
            projectId.ToString());

    /// <summary>Returns incomplete milestones due within <paramref name="days"/> from now.</summary>
    public async Task<IReadOnlyList<Milestone>> GetUpcomingAsync(Guid projectId, int days = 30)
    {
        DateTime cutoff = DateTime.UtcNow.AddDays(days);
        return await Connection.QueryAsync<Milestone>(
            "SELECT * FROM Milestones WHERE ProjectId = ? AND IsComplete = 0 AND DueDate <= ? ORDER BY DueDate",
            projectId.ToString(), cutoff);
    }

    public async Task InsertAsync(Milestone milestone)
        => await Connection.InsertAsync(milestone);

    public async Task UpdateAsync(Milestone milestone)
        => await Connection.UpdateAsync(milestone);

    public async Task DeleteAsync(Guid id)
        => await Connection.DeleteAsync<Milestone>(id.ToString());
}
