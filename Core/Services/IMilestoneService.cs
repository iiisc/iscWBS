using iscWBS.Core.Models;

namespace iscWBS.Core.Services;

/// <summary>Business logic for <see cref="Milestone"/> CRUD operations.</summary>
public interface IMilestoneService
{
    /// <summary>Returns all milestones for a project ordered by due date.</summary>
    Task<IReadOnlyList<Milestone>> GetByProjectAsync(Guid projectId);

    /// <summary>Creates a new milestone and returns the persisted entity.</summary>
    Task<Milestone> CreateAsync(Guid projectId, string title, DateTime dueDate);

    /// <summary>Persists changes to an existing milestone.</summary>
    Task UpdateAsync(Milestone milestone);

    /// <summary>Deletes a milestone by id.</summary>
    Task DeleteAsync(Guid id);

    /// <summary>Marks a milestone as complete.</summary>
    Task MarkCompleteAsync(Guid id);

    /// <summary>Returns incomplete milestones due within <paramref name="days"/> days from now, ordered by due date.</summary>
    Task<IReadOnlyList<Milestone>> GetUpcomingAsync(Guid projectId, int days = 30);
}
