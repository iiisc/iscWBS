using iscWBS.Core.Models;
using iscWBS.Core.Services;
using Microsoft.Data.Sqlite;
using iscWBS.Core.Data;

using static iscWBS.Tests.TestData;

namespace iscWBS.Tests;

public sealed class WbsServiceDatabaseTests : IAsyncLifetime
{
    private readonly TempDirectory _dir = new();
    private readonly ProjectStateService _state;
    private readonly WbsService _service;
    private readonly MilestoneService _milestones;
    private Guid _projectId;

    public WbsServiceDatabaseTests()
    {
        _state = new ProjectStateService(new SettingsService(_dir.File("settings.json")));
        _service = new WbsService(_state);
        _milestones = new MilestoneService(_state);
    }

    private SqliteConnection Db => _state.Connection;

    public async ValueTask InitializeAsync()
    {
        await _state.CreateProjectAsync("Test", _dir.File("test.iscwbs"));
        _projectId = _state.ActiveProject!.Id;
    }

    public async ValueTask DisposeAsync()
    {
        await _state.CloseProjectAsync();
        _dir.Dispose();
    }

    private async Task<string> CodeOf(WbsNode node) => (await _service.GetByIdAsync(node.Id))!.Code;

    [Fact]
    public async Task GetAll_ReturnsOutlineOrder_So1_10ComesAfter1_9()
    {
        WbsNode root = await _service.AddRootNodeAsync(_projectId, "Root");
        for (int i = 0; i < 10; i++)
            await _service.AddChildNodeAsync(root.Id, $"Child {i + 1}");

        IReadOnlyList<WbsNode> all = await _service.GetAllByProjectAsync(_projectId);

        Assert.Equal(["1", "1.1", "1.2", "1.3", "1.4", "1.5", "1.6", "1.7", "1.8", "1.9", "1.10"], all.Select(n => n.Code));
    }

    [Fact]
    public async Task Inserts_AssignOutlineCodes_AndSiblingInsertRenumbersLaterSiblingsAndTheirChildren()
    {
        WbsNode root = await _service.AddRootNodeAsync(_projectId, "Root");
        WbsNode first = await _service.AddChildNodeAsync(root.Id, "First");
        WbsNode second = await _service.AddChildNodeAsync(root.Id, "Second");
        WbsNode secondChild = await _service.AddChildNodeAsync(second.Id, "Second child");
        WbsNode root2 = await _service.AddRootNodeAsync(_projectId, "Root 2");

        Assert.Equal(["1", "1.1", "1.2", "1.2.1", "2"], [root.Code, first.Code, second.Code, secondChild.Code, root2.Code]);

        WbsNode inserted = await _service.AddSiblingNodeAsync(first.Id, "Inserted");

        Assert.Equal("1.2", inserted.Code);
        Assert.Equal("1.1", await CodeOf(first));
        Assert.Equal("1.3", await CodeOf(second));
        Assert.Equal("1.3.1", await CodeOf(secondChild));
        Assert.Equal("2", await CodeOf(root2));
    }

    [Fact]
    public async Task Delete_RemovesSubtreeWithItsDependenciesAndMilestoneLinks_AndRecodesSiblings()
    {
        WbsNode root = await _service.AddRootNodeAsync(_projectId, "Root");
        WbsNode doomed = await _service.AddChildNodeAsync(root.Id, "Doomed");
        WbsNode doomedChild = await _service.AddChildNodeAsync(doomed.Id, "Doomed child");
        WbsNode keptA = await _service.AddChildNodeAsync(root.Id, "Kept A");
        WbsNode keptB = await _service.AddChildNodeAsync(root.Id, "Kept B");
        WbsNode other = await _service.AddRootNodeAsync(_projectId, "Other");

        await _service.AddDependencyAsync(new() { PredecessorId = doomedChild.Id, SuccessorId = other.Id });
        await _service.AddDependencyAsync(new() { PredecessorId = other.Id, SuccessorId = doomed.Id });
        await _service.AddDependencyAsync(new() { PredecessorId = keptA.Id, SuccessorId = keptB.Id });

        Milestone milestone = await _milestones.CreateAsync(_projectId, "M", DateTime.Today);
        await _milestones.LinkNodeAsync(milestone.Id, doomedChild.Id);
        await _milestones.LinkNodeAsync(milestone.Id, keptB.Id);

        await _service.DeleteNodeAsync(doomed.Id);

        IReadOnlyList<WbsNode> remaining = await _service.GetAllByProjectAsync(_projectId);
        Assert.Equal(
            [(root.Id, "1"), (keptA.Id, "1.1"), (keptB.Id, "1.2"), (other.Id, "2")],
            remaining.Select(n => (n.Id, n.Code)));

        // Read the raw table: the project-scoped query joins on the successor and would hide orphans.
        var deps = await Db.QueryAsync("SELECT PredecessorId, SuccessorId FROM NodeDependencies",
            r => (r.GetGuidText(0), r.GetGuidText(1)));
        Assert.Equal([(keptA.Id, keptB.Id)], deps);

        Assert.Equal([keptB.Id], await _milestones.GetLinkedNodeIdsAsync(milestone.Id));
    }

    [Fact]
    public async Task AddDependency_RejectsSelfDuplicateAndCycles()
    {
        WbsNode a = await _service.AddRootNodeAsync(_projectId, "A");
        WbsNode b = await _service.AddRootNodeAsync(_projectId, "B");
        WbsNode c = await _service.AddRootNodeAsync(_projectId, "C");
        await _service.AddDependencyAsync(new() { PredecessorId = a.Id, SuccessorId = b.Id });
        await _service.AddDependencyAsync(new() { PredecessorId = b.Id, SuccessorId = c.Id, Type = DependencyType.StartToStart });

        await AssertFailsWith("Error_SelfDependency",
            () => _service.AddDependencyAsync(new() { PredecessorId = a.Id, SuccessorId = a.Id }));
        await AssertFailsWith("Error_DuplicateDependency",
            () => _service.AddDependencyAsync(new() { PredecessorId = a.Id, SuccessorId = b.Id, Type = DependencyType.FinishToFinish }));
        await AssertFailsWith("Error_CircularDependency",
            () => _service.AddDependencyAsync(new() { PredecessorId = b.Id, SuccessorId = a.Id }));
        await AssertFailsWith("Error_CircularDependency",
            () => _service.AddDependencyAsync(new() { PredecessorId = c.Id, SuccessorId = a.Id }));

        await _service.AddDependencyAsync(new() { PredecessorId = a.Id, SuccessorId = c.Id });
        Assert.Equal(3, (await _service.GetAllDependenciesByProjectAsync(_projectId)).Count);
    }

    [Fact]
    public async Task AddDependency_ToUnknownSuccessor_IsNotFound()
    {
        WbsNode a = await _service.AddRootNodeAsync(_projectId, "A");

        await AssertFailsWith("Error_NodeNotFound",
            () => _service.AddDependencyAsync(new() { PredecessorId = a.Id, SuccessorId = Guid.NewGuid() }));
    }

    [Fact]
    public async Task UpdateNode_WithInstanceLoadedBeforeARenumber_KeepsTheCurrentOutlinePosition()
    {
        WbsNode root = await _service.AddRootNodeAsync(_projectId, "Root");
        WbsNode first = await _service.AddChildNodeAsync(root.Id, "First");
        WbsNode second = await _service.AddChildNodeAsync(root.Id, "Second");

        // The detail panel keeps the instance it loaded while edits elsewhere renumber the tree.
        await _service.AddSiblingNodeAsync(first.Id, "Inserted");
        second.Title = "Second renamed";
        second.DueDate = new DateTime(2026, 11, 20);
        await _service.UpdateNodeAsync(second);

        WbsNode saved = (await _service.GetByIdAsync(second.Id))!;
        Assert.Equal(("Second renamed", new DateTime(2026, 11, 20), "1.3", 2), (saved.Title, saved.DueDate, saved.Code, saved.SortOrder));
    }

    [Fact]
    public async Task MarkComplete_UnknownMilestone_IsMilestoneNotFound()
        => await AssertFailsWith("Error_MilestoneNotFound", () => _milestones.MarkCompleteAsync(Guid.NewGuid()));
}
