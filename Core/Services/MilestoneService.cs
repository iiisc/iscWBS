using iscWBS.Core.Exceptions;
using iscWBS.Core.Models;
using iscWBS.Core.Repositories;

namespace iscWBS.Core.Services;

public sealed class MilestoneService : IMilestoneService
{
    private readonly MilestoneRepository _repository;

    public MilestoneService(MilestoneRepository repository)
    {
        _repository = repository;
    }

    public Task<IReadOnlyList<Milestone>> GetByProjectAsync(Guid projectId)
        => _repository.GetByProjectAsync(projectId);

    public async Task<Milestone> CreateAsync(Guid projectId, string title, DateTime dueDate)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new WbsValidationException("Milestone title is required.");

        var milestone = new Milestone
        {
            ProjectId = projectId,
            Title = title.Trim(),
            DueDate = dueDate
        };
        await _repository.InsertAsync(milestone);
        return milestone;
    }

    public Task UpdateAsync(Milestone milestone)
        => _repository.UpdateAsync(milestone);

    public Task DeleteAsync(Guid id)
        => _repository.DeleteAsync(id);

    public async Task MarkCompleteAsync(Guid id)
    {
        Milestone? milestone = await _repository.GetByIdAsync(id)
            ?? throw new WbsNotFoundException(id);
        milestone.IsComplete = true;
        await _repository.UpdateAsync(milestone);
    }
}
