using iscWBS.Core.Exceptions;
using iscWBS.Core.Models;
using iscWBS.Core.Repositories;

namespace iscWBS.Core.Services;

public sealed class MilestoneService : IMilestoneService
{
    private readonly MilestoneRepository _repository;
    private readonly MilestoneNodeLinkRepository _linkRepository;

    public MilestoneService(MilestoneRepository repository, MilestoneNodeLinkRepository linkRepository)
    {
        _repository = repository;
        _linkRepository = linkRepository;
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

    public async Task DeleteAsync(Guid id)
    {
        await _linkRepository.DeleteByMilestoneAsync(id);
        await _repository.DeleteAsync(id);
    }

    public async Task MarkCompleteAsync(Guid id)
    {
        Milestone? milestone = await _repository.GetByIdAsync(id)
            ?? throw new WbsNotFoundException(id);
        milestone.IsComplete = true;
        await _repository.UpdateAsync(milestone);
    }

    public Task<IReadOnlyList<Milestone>> GetUpcomingAsync(Guid projectId, int days = 30)
        => _repository.GetUpcomingAsync(projectId, days);

    public async Task<IReadOnlyList<Guid>> GetLinkedNodeIdsAsync(Guid milestoneId)
    {
        IReadOnlyList<MilestoneNodeLink> links = await _linkRepository.GetByMilestoneAsync(milestoneId);
        return links.Select(l => l.NodeId).ToList();
    }

    public Task<IReadOnlyDictionary<Guid, int>> GetLinkedCountsByProjectAsync(Guid projectId)
        => _linkRepository.GetCountsByProjectAsync(projectId);

    public async Task LinkNodeAsync(Guid milestoneId, Guid nodeId)
    {
        IReadOnlyList<MilestoneNodeLink> existing = await _linkRepository.GetByMilestoneAsync(milestoneId);
        if (existing.Any(l => l.NodeId == nodeId))
            return;

        await _linkRepository.InsertAsync(new MilestoneNodeLink
        {
            MilestoneId = milestoneId,
            NodeId = nodeId
        });
    }

    public async Task UnlinkNodeAsync(Guid milestoneId, Guid nodeId)
    {
        IReadOnlyList<MilestoneNodeLink> links = await _linkRepository.GetByMilestoneAsync(milestoneId);
        MilestoneNodeLink? link = links.FirstOrDefault(l => l.NodeId == nodeId);
        if (link is not null)
            await _linkRepository.DeleteAsync(link.Id);
    }

    public Task<IReadOnlyList<Guid>> GetMilestoneIdsForNodeAsync(Guid nodeId)
        => _linkRepository.GetMilestoneIdsByNodeAsync(nodeId);
}

