using iscWBS.Core.Models;

namespace iscWBS.Core.Services;

/// <summary>Stub implementation — full WBS logic implemented in Phase 2.</summary>
public sealed class WbsService : IWbsService
{
    public Task<IReadOnlyList<WbsNode>> GetRootNodesAsync(Guid projectId)
        => Task.FromResult<IReadOnlyList<WbsNode>>(Array.Empty<WbsNode>());

    public Task<IReadOnlyList<WbsNode>> GetChildrenAsync(Guid parentId)
        => Task.FromResult<IReadOnlyList<WbsNode>>(Array.Empty<WbsNode>());

    public Task<bool> HasChildrenAsync(Guid nodeId)
        => Task.FromResult(false);

    public Task<WbsNode> AddChildNodeAsync(Guid parentId, string title)
        => throw new NotImplementedException("Implemented in Phase 2.");

    public Task<WbsNode> AddSiblingNodeAsync(Guid siblingId, string title)
        => throw new NotImplementedException("Implemented in Phase 2.");

    public Task UpdateNodeAsync(WbsNode node)
        => throw new NotImplementedException("Implemented in Phase 2.");

    public Task DeleteNodeAsync(Guid id)
        => throw new NotImplementedException("Implemented in Phase 2.");

    public Task MoveNodeAsync(Guid nodeId, Guid? newParentId, int newSortOrder)
        => throw new NotImplementedException("Implemented in Phase 2.");
}
