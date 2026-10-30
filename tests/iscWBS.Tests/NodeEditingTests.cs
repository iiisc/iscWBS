using iscWBS.Core.Data;
using iscWBS.Core.Models;
using iscWBS.Core.Services;

namespace iscWBS.Tests;

/// <summary>Moving nodes, notes, the baseline and rescheduling, against a real project file.</summary>
public sealed class NodeEditingTests : IAsyncLifetime
{
    private readonly TempDirectory _dir = new();
    private readonly ProjectStateService _state;
    private readonly WbsService _wbs;
    private Guid _projectId;

    public NodeEditingTests()
    {
        _state = new ProjectStateService(new SettingsService(_dir.File("settings.json")));
        _wbs = new WbsService(_state);
    }

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

    private async Task<string[]> Outline() => (await _wbs.GetAllByProjectAsync(_projectId)).Select(n => $"{n.Code} {n.Title}").ToArray();

    [Fact]
    public async Task Move_ToAnotherParent_TakesTheBranchAlong_AndRenumbersBothSides()
    {
        WbsNode root = await _wbs.AddRootNodeAsync(_projectId, "Root");
        WbsNode a = await _wbs.AddChildNodeAsync(root.Id, "A");
        WbsNode a1 = await _wbs.AddChildNodeAsync(a.Id, "A1");
        await _wbs.AddChildNodeAsync(a1.Id, "A1x");
        await _wbs.AddChildNodeAsync(a.Id, "A2");
        WbsNode b = await _wbs.AddChildNodeAsync(root.Id, "B");
        await _wbs.AddChildNodeAsync(b.Id, "B1");

        await _wbs.MoveNodeAsync(a1.Id, b.Id, 0);

        Assert.Equal(["1 Root", "1.1 A", "1.1.1 A2", "1.2 B", "1.2.1 A1", "1.2.1.1 A1x", "1.2.2 B1"], await Outline());
    }

    [Fact]
    public async Task Move_AmongSiblings_ChangesTheOrder()
    {
        WbsNode root = await _wbs.AddRootNodeAsync(_projectId, "Root");
        await _wbs.AddChildNodeAsync(root.Id, "A");
        await _wbs.AddChildNodeAsync(root.Id, "B");
        WbsNode c = await _wbs.AddChildNodeAsync(root.Id, "C");

        await _wbs.MoveNodeAsync(c.Id, root.Id, 0);

        Assert.Equal(["1 Root", "1.1 C", "1.2 A", "1.3 B"], await Outline());
    }

    [Fact]
    public async Task Move_ToTheTopLevel_AndIntoItsOwnBranch()
    {
        WbsNode root = await _wbs.AddRootNodeAsync(_projectId, "Root");
        WbsNode a = await _wbs.AddChildNodeAsync(root.Id, "A");
        WbsNode a1 = await _wbs.AddChildNodeAsync(a.Id, "A1");

        await TestData.AssertFailsWith("Error_MoveIntoOwnBranch", () => _wbs.MoveNodeAsync(a.Id, a1.Id, 0));
        await _wbs.MoveNodeAsync(a.Id, null, 1);

        Assert.Equal(["1 Root", "2 A", "2.1 A1"], await Outline());
    }

    [Fact]
    public async Task Notes_ListNewestFirst_AndGoWithTheirBranch()
    {
        WbsNode root = await _wbs.AddRootNodeAsync(_projectId, "Root");
        WbsNode task = await _wbs.AddChildNodeAsync(root.Id, "Task");
        await _wbs.AddNoteAsync(task.Id, "First");
        NodeNote second = await _wbs.AddNoteAsync(task.Id, "  Second  ");

        Assert.Equal(["Second", "First"], (await _wbs.GetNotesAsync(task.Id)).Select(n => n.Text));
        Assert.Equal(2, (await _wbs.GetNotesSinceAsync(_projectId, DateTime.Today)).Count);
        await TestData.AssertFailsWith("Error_NoteTextRequired", () => _wbs.AddNoteAsync(task.Id, " "));

        await _wbs.DeleteNoteAsync(second.Id);
        Assert.Equal(["First"], (await _wbs.GetNotesAsync(task.Id)).Select(n => n.Text));

        await _wbs.DeleteNodeAsync(root.Id);
        Assert.Equal(0, await _state.Connection.ScalarAsync<int>("SELECT COUNT(*) FROM NodeNotes"));
    }

    [Fact]
    public async Task SetBaseline_CopiesTheCurrentDates_WhichLaterEditsLeaveAlone()
    {
        WbsNode root = await _wbs.AddRootNodeAsync(_projectId, "Root");
        root.StartDate = new DateTime(2026, 3, 2);
        root.DueDate = new DateTime(2026, 3, 20);
        await _wbs.UpdateNodeAsync(root);

        await _wbs.SetBaselineAsync(_projectId);
        root.DueDate = new DateTime(2026, 4, 1);
        await _wbs.UpdateNodeAsync(root);

        WbsNode saved = (await _wbs.GetByIdAsync(root.Id))!;
        Assert.Equal((new DateTime(2026, 3, 2), new DateTime(2026, 3, 20), new DateTime(2026, 4, 1)),
            (saved.BaselineStart, saved.BaselineDue, saved.DueDate));
    }

    [Fact]
    public async Task ChangingDates_OrAddingADependency_MovesWaitingWorkLater_AsOneUndoStep()
    {
        WbsNode root = await _wbs.AddRootNodeAsync(_projectId, "Root");
        WbsNode design = await _wbs.AddChildNodeAsync(root.Id, "Design");
        WbsNode build = await _wbs.AddChildNodeAsync(root.Id, "Build");
        (design.StartDate, design.DueDate) = (new DateTime(2026, 3, 2), new DateTime(2026, 3, 10));
        (build.StartDate, build.DueDate) = (new DateTime(2026, 3, 5), new DateTime(2026, 3, 9));
        await _wbs.UpdateNodeAsync(design);
        await _wbs.UpdateNodeAsync(build);

        IReadOnlyList<WbsNode> moved = await _wbs.AddDependencyAsync(new NodeDependency { PredecessorId = design.Id, SuccessorId = build.Id });
        Assert.Equal([build.Id], moved.Select(n => n.Id));
        Assert.Equal(new DateTime(2026, 3, 10), (await _wbs.GetByIdAsync(build.Id))!.StartDate);

        design.DueDate = new DateTime(2026, 3, 16);
        await _wbs.UpdateNodeAsync(design);
        Assert.Equal((new DateTime(2026, 3, 16), new DateTime(2026, 3, 20)),
            ((await _wbs.GetByIdAsync(build.Id))!.StartDate, (await _wbs.GetByIdAsync(build.Id))!.DueDate));

        await _state.History.UndoAsync();
        Assert.Equal(new DateTime(2026, 3, 10), (await _wbs.GetByIdAsync(design.Id))!.DueDate);
        Assert.Equal(new DateTime(2026, 3, 10), (await _wbs.GetByIdAsync(build.Id))!.StartDate);
    }
}
