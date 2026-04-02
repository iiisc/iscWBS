using iscWBS.Core.Exceptions;
using iscWBS.Core.Models;
using iscWBS.Core.Repositories;

namespace iscWBS.Core.Services;

public sealed class WbsService : IWbsService
{
    private readonly WbsNodeRepository _repository;
    private readonly NodeDependencyRepository _dependencyRepository;
    private readonly IProjectStateService _projectStateService;

    public WbsService(
        WbsNodeRepository repository,
        NodeDependencyRepository dependencyRepository,
        IProjectStateService projectStateService)
    {
        _repository = repository;
        _dependencyRepository = dependencyRepository;
        _projectStateService = projectStateService;
    }

    public Task<WbsNode?> GetByIdAsync(Guid id)
        => _repository.GetByIdAsync(id);

    public Task<IReadOnlyList<WbsNode>> GetRootNodesAsync(Guid projectId)
        => _repository.GetRootNodesAsync(projectId);

    public Task<IReadOnlyList<WbsNode>> GetChildrenAsync(Guid parentId)
        => _repository.GetChildrenAsync(parentId);

    public Task<bool> HasChildrenAsync(Guid nodeId)
        => _repository.HasChildrenAsync(nodeId);

    public Task<IReadOnlyList<WbsNode>> GetAllByProjectAsync(Guid projectId)
        => _repository.GetAllByProjectAsync(projectId);

    public async Task<WbsNode> AddRootNodeAsync(Guid projectId, string title)
    {
        IReadOnlyList<WbsNode> roots = await _repository.GetRootNodesAsync(projectId);
        var node = new WbsNode
        {
            ProjectId = projectId,
            Title = title,
            SortOrder = roots.Count
        };
        await _repository.InsertAsync(node);
        await RecodeAsync(null, projectId, string.Empty);
        return await _repository.GetByIdAsync(node.Id) ?? throw new WbsNotFoundException(node.Id);
    }

    public async Task<WbsNode> AddChildNodeAsync(Guid parentId, string title)
    {
        WbsNode parent = await _repository.GetByIdAsync(parentId)
            ?? throw new WbsNotFoundException(parentId);
        IReadOnlyList<WbsNode> children = await _repository.GetChildrenAsync(parentId);
        var node = new WbsNode
        {
            ProjectId = parent.ProjectId,
            ParentId = parentId,
            Title = title,
            SortOrder = children.Count
        };
        await _repository.InsertAsync(node);
        await RecodeAsync(parentId, parent.ProjectId, parent.Code);
        return await _repository.GetByIdAsync(node.Id) ?? throw new WbsNotFoundException(node.Id);
    }

    public async Task<WbsNode> AddSiblingNodeAsync(Guid siblingId, string title)
    {
        WbsNode sibling = await _repository.GetByIdAsync(siblingId)
            ?? throw new WbsNotFoundException(siblingId);

        Guid projectId = sibling.ProjectId;
        Guid? parentId = sibling.ParentId;
        int insertAt = sibling.SortOrder + 1;

        IReadOnlyList<WbsNode> siblings = parentId.HasValue
            ? await _repository.GetChildrenAsync(parentId.Value)
            : await _repository.GetRootNodesAsync(projectId);

        foreach (WbsNode n in siblings.Where(n => n.SortOrder >= insertAt))
        {
            n.SortOrder++;
            await _repository.UpdateAsync(n);
        }

        var node = new WbsNode
        {
            ProjectId = projectId,
            ParentId = parentId,
            Title = title,
            SortOrder = insertAt
        };
        await _repository.InsertAsync(node);

        string parentCode = parentId.HasValue
            ? (await _repository.GetByIdAsync(parentId.Value))?.Code ?? string.Empty
            : string.Empty;
        await RecodeAsync(parentId, projectId, parentCode);
        return await _repository.GetByIdAsync(node.Id) ?? throw new WbsNotFoundException(node.Id);
    }

    public Task UpdateNodeAsync(WbsNode node)
        => _repository.UpdateAsync(node);

    public async Task DeleteNodeAsync(Guid id)
    {
        WbsNode node = await _repository.GetByIdAsync(id)
            ?? throw new WbsNotFoundException(id);

        Guid? parentId = node.ParentId;
        Guid projectId = node.ProjectId;
        string parentCode = parentId.HasValue
            ? (await _repository.GetByIdAsync(parentId.Value))?.Code ?? string.Empty
            : string.Empty;

        await DeleteRecursiveAsync(id);
        await RecodeAsync(parentId, projectId, parentCode);
    }

    public async Task MoveNodeAsync(Guid nodeId, Guid? newParentId, int newSortOrder)
    {
        WbsNode node = await _repository.GetByIdAsync(nodeId)
            ?? throw new WbsNotFoundException(nodeId);

        Guid projectId = node.ProjectId;
        Guid? oldParentId = node.ParentId;
        string oldParentCode = oldParentId.HasValue
            ? (await _repository.GetByIdAsync(oldParentId.Value))?.Code ?? string.Empty
            : string.Empty;

        // Compact old sibling group (excluding the node being moved)
        IReadOnlyList<WbsNode> oldSiblings = oldParentId.HasValue
            ? await _repository.GetChildrenAsync(oldParentId.Value)
            : await _repository.GetRootNodesAsync(projectId);

        List<WbsNode> oldGroup = oldSiblings
            .Where(n => n.Id != nodeId)
            .OrderBy(n => n.SortOrder)
            .ToList();
        for (int i = 0; i < oldGroup.Count; i++)
        {
            oldGroup[i].SortOrder = i;
            await _repository.UpdateAsync(oldGroup[i]);
        }

        // Get new sibling group
        List<WbsNode> newGroup;
        string newParentCode;
        if (oldParentId == newParentId)
        {
            newGroup = oldGroup;
            newParentCode = oldParentCode;
        }
        else
        {
            IReadOnlyList<WbsNode> newSiblings = newParentId.HasValue
                ? await _repository.GetChildrenAsync(newParentId.Value)
                : await _repository.GetRootNodesAsync(projectId);
            newGroup = newSiblings.OrderBy(n => n.SortOrder).ToList();
            newParentCode = newParentId.HasValue
                ? (await _repository.GetByIdAsync(newParentId.Value))?.Code ?? string.Empty
                : string.Empty;
        }

        newSortOrder = Math.Clamp(newSortOrder, 0, newGroup.Count);
        foreach (WbsNode n in newGroup.Where(n => n.SortOrder >= newSortOrder))
        {
            n.SortOrder++;
            await _repository.UpdateAsync(n);
        }

        node.ParentId = newParentId;
        node.SortOrder = newSortOrder;
        await _repository.UpdateAsync(node);

        await RecodeAsync(oldParentId, projectId, oldParentCode);
        if (oldParentId != newParentId)
            await RecodeAsync(newParentId, projectId, newParentCode);
    }

    private async Task DeleteRecursiveAsync(Guid nodeId)
    {
        IReadOnlyList<WbsNode> children = await _repository.GetChildrenAsync(nodeId);
        foreach (WbsNode child in children)
            await DeleteRecursiveAsync(child.Id);
        await _dependencyRepository.DeleteByNodeAsync(nodeId);
        await _repository.DeleteAsync(nodeId);
    }

    private async Task RecodeAsync(Guid? parentId, Guid projectId, string parentCode)
    {
        IReadOnlyList<WbsNode> children = parentId.HasValue
            ? await _repository.GetChildrenAsync(parentId.Value)
            : await _repository.GetRootNodesAsync(projectId);

        List<WbsNode> ordered = children.OrderBy(n => n.SortOrder).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            WbsNode node = ordered[i];
            string newCode = string.IsNullOrEmpty(parentCode)
                ? $"{i + 1}"
                : $"{parentCode}.{i + 1}";

            bool codeChanged = node.Code != newCode;
            node.SortOrder = i;
            node.Code = newCode;
            await _repository.UpdateAsync(node);

            if (codeChanged)
                await RecodeAsync(node.Id, projectId, newCode);
        }
    }

    public Task<IReadOnlyList<NodeDependency>> GetDependenciesAsync(Guid nodeId)
        => _dependencyRepository.GetBySuccessorAsync(nodeId);

    public Task AddDependencyAsync(NodeDependency dependency)
        => _dependencyRepository.InsertAsync(dependency);

    public Task RemoveDependencyAsync(int id)
        => _dependencyRepository.DeleteAsync(id);
}
