using iscWBS.Core.Models;
using iscWBS.Core.Services;

namespace iscWBS.Tests;

public sealed class ProjectHistoryTests : IAsyncLifetime
{
    private readonly TempDirectory _dir = new();
    private readonly ProjectStateService _state;
    private readonly WbsService _wbs;
    private readonly MilestoneService _milestones;
    private Guid _projectId;

    public ProjectHistoryTests()
    {
        _state = new ProjectStateService(new SettingsService(_dir.File("settings.json")));
        _wbs = new WbsService(_state);
        _milestones = new MilestoneService(_state);
    }

    private ProjectHistory History => _state.History;

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

    private async Task<string[]> Codes() => (await _wbs.GetAllByProjectAsync(_projectId)).Select(n => $"{n.Code} {n.Title}").ToArray();

    [Fact]
    public async Task UndoingADelete_BringsBackTheBranchWithItsDependenciesLinksAndNotes()
    {
        WbsNode root = await _wbs.AddRootNodeAsync(_projectId, "Root");
        WbsNode branch = await _wbs.AddChildNodeAsync(root.Id, "Branch");
        WbsNode leaf = await _wbs.AddChildNodeAsync(branch.Id, "Leaf");
        WbsNode other = await _wbs.AddChildNodeAsync(root.Id, "Other");
        await _wbs.AddDependencyAsync(new NodeDependency { PredecessorId = leaf.Id, SuccessorId = other.Id });
        Milestone milestone = await _milestones.CreateAsync(_projectId, "Go-live", new DateTime(2026, 12, 1));
        await _milestones.LinkNodeAsync(milestone.Id, leaf.Id);
        await _wbs.AddNoteAsync(leaf.Id, "Agreed with the supplier");
        string[] before = await Codes();

        await _wbs.DeleteNodeAsync(branch.Id);
        Assert.True(await History.UndoAsync());

        Assert.Equal(before, await Codes());
        Assert.Single(await _wbs.GetAllDependenciesByProjectAsync(_projectId));
        Assert.Equal([leaf.Id], await _milestones.GetLinkedNodeIdsAsync(milestone.Id));
        Assert.Equal("Agreed with the supplier", Assert.Single(await _wbs.GetNotesAsync(leaf.Id)).Text);
    }

    [Fact]
    public async Task Redo_ReappliesWhatWasUndone_AndANewChangeClearsIt()
    {
        WbsNode root = await _wbs.AddRootNodeAsync(_projectId, "Root");
        root.Title = "Renamed";
        await _wbs.UpdateNodeAsync(root);

        await History.UndoAsync();
        Assert.Equal(["1 Root"], await Codes());
        Assert.True(History.CanRedo);

        await History.RedoAsync();
        Assert.Equal(["1 Renamed"], await Codes());

        await History.UndoAsync();
        await _wbs.AddChildNodeAsync(root.Id, "Child");
        Assert.False(History.CanRedo);
    }

    [Fact]
    public async Task KeepsTheLatestSteps_UpToItsDepth()
    {
        WbsNode root = await _wbs.AddRootNodeAsync(_projectId, "Root");
        for (int i = 0; i < ProjectHistory.Depth + 5; i++)
        {
            root.Title = $"Title {i}";
            await _wbs.UpdateNodeAsync(root);
        }

        int undone = 0;
        while (await History.UndoAsync())
            undone++;

        Assert.Equal(ProjectHistory.Depth, undone);
        Assert.Equal(["1 Title 4"], await Codes());
    }

    [Fact]
    public async Task OpeningAnotherProject_ForgetsTheHistory()
    {
        await _wbs.AddRootNodeAsync(_projectId, "Root");
        Assert.True(History.CanUndo);

        await _state.CreateProjectAsync("Other", _dir.File("other.iscwbs"));

        Assert.False(History.CanUndo);
    }
}
