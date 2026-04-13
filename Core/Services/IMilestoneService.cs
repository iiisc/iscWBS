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

    /// <summary>Deletes a milestone and all its node links by id.</summary>
    Task DeleteAsync(Guid id);

    /// <summary>Marks a milestone as complete.</summary>
    Task MarkCompleteAsync(Guid id);

    /// <summary>Returns incomplete milestones due within <paramref name="days"/> days from now, ordered by due date.</summary>
    Task<IReadOnlyList<Milestone>> GetUpcomingAsync(Guid projectId, int days = 30);

    /// <summary>Returns the IDs of all WBS nodes linked to the given milestone.</summary>
    Task<IReadOnlyList<Guid>> GetLinkedNodeIdsAsync(Guid milestoneId);

    /// <summary>
    /// Returns a dictionary mapping milestone ID to linked-node count for all milestones in the
    /// given project. Milestones with zero links are absent from the result.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, int>> GetLinkedCountsByProjectAsync(Guid projectId);

    /// <summary>Links a WBS node to a milestone. A duplicate link is silently ignored.</summary>
    Task LinkNodeAsync(Guid milestoneId, Guid nodeId);

    /// <summary>Removes the link between a milestone and a WBS node.</summary>
    Task UnlinkNodeAsync(Guid milestoneId, Guid nodeId);

    /// <summary>Returns the IDs of all milestones that link to the given WBS node.</summary>
    Task<IReadOnlyList<Guid>> GetMilestoneIdsForNodeAsync(Guid nodeId);
}

