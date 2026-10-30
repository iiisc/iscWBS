using iscWBS.Core.Data;
using iscWBS.Core.Models;
using iscWBS.Core.Services;

namespace iscWBS.Tests;

public sealed class WbsDatabaseTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly SettingsService _settings;

    public WbsDatabaseTests() => _settings = new SettingsService(_dir.File("settings.json"));

    public void Dispose() => _dir.Dispose();

    private string CreateForeignSqliteFile(int applicationId)
    {
        string path = _dir.File("foreign.db");
        using var connection = TestData.OpenRaw(path);
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA application_id = {applicationId}; CREATE TABLE Things (Id INTEGER PRIMARY KEY)";
        command.ExecuteNonQuery();
        return path;
    }

    [Fact]
    public async Task Initialize_RejectsSqliteFileStampedByAnotherApplication()
    {
        var db = new WbsDatabase(CreateForeignSqliteFile(applicationId: 12345));
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(db.InitializeAsync);
        }
        finally
        {
            await db.CloseAsync();
        }
    }

    [Fact]
    public async Task OpenProject_RejectsForeignFile_AndLeavesNoProjectOpen()
    {
        string path = CreateForeignSqliteFile(applicationId: 12345);
        var state = new ProjectStateService(_settings);

        await Assert.ThrowsAsync<InvalidOperationException>(() => state.OpenProjectAsync(path));

        Assert.False(state.HasActiveProject);
        Assert.Empty(_settings.GetRecentProjects());
        File.Delete(path); // Throws if the connection was left open.
    }

    [Fact]
    public async Task OpenProject_RejectsUnstampedForeignFile_WithoutModifyingIt()
    {
        string path = CreateForeignSqliteFile(applicationId: 0);
        var state = new ProjectStateService(_settings);

        await Assert.ThrowsAsync<InvalidOperationException>(() => state.OpenProjectAsync(path));

        Assert.False(state.HasActiveProject);
        using (var connection = TestData.OpenRaw(path))
        {
            Assert.Equal(0, await connection.ScalarAsync<int>("PRAGMA application_id"));
            Assert.Equal(0, await connection.ScalarAsync<int>("SELECT COUNT(*) FROM sqlite_master WHERE name = 'Projects'"));
            Assert.Equal("delete", await connection.ScalarAsync<string>("PRAGMA journal_mode"));
        }
        File.Delete(path);
    }

    [Fact]
    public async Task OpenProject_WithoutProjectRow_IsRejectedAndReleasesTheConnection()
    {
        string path = _dir.File("empty.iscwbs");
        var state = new ProjectStateService(_settings);
        await state.CreateProjectAsync("Temp", path);
        await state.CloseProjectAsync();
        using (var connection = TestData.OpenRaw(path))
            await connection.ExecuteAsync("DELETE FROM Projects");

        await TestData.AssertFailsWith("Error_InvalidProjectData", () => state.OpenProjectAsync(path));

        Assert.False(state.HasActiveProject);
        File.Delete(path);
    }

    [Fact]
    public async Task OpenProject_RejectedFile_KeepsTheCurrentProjectOpen()
    {
        var state = new ProjectStateService(_settings);
        await state.CreateProjectAsync("Current", _dir.File("current.iscwbs"));
        string junk = _dir.File("junk.iscwbs");
        File.WriteAllText(junk, "not a database");

        await Assert.ThrowsAsync<InvalidOperationException>(() => state.OpenProjectAsync(junk));

        Assert.Equal("Current", state.ActiveProject?.Name);
        Assert.Equal(1, await state.Connection.ScalarAsync<int>("SELECT COUNT(*) FROM Projects"));
        await state.CloseProjectAsync();
        File.Delete(junk);
    }

    [Fact]
    public async Task CreateProject_OverAnExistingFile_IsRefusedAndLeavesItAlone()
    {
        string path = _dir.File("alpha.iscwbs");
        var state = new ProjectStateService(_settings);
        await state.CreateProjectAsync("Alpha", path);

        await TestData.AssertFailsWith("Error_ProjectFileExists", () => state.CreateProjectAsync("Alpha", path));

        Assert.Equal("Alpha", state.ActiveProject?.Name);
        Assert.Equal(1, await state.Connection.ScalarAsync<int>("SELECT COUNT(*) FROM Projects"));
        await state.CloseProjectAsync();
    }

    [Fact]
    public async Task SavedChanges_AreInTheProjectFileWhileItIsOpen()
    {
        string path = _dir.File("shared.iscwbs");
        var state = new ProjectStateService(_settings);
        await state.CreateProjectAsync("Shared", path);
        await state.CloseProjectAsync();
        using (var connection = TestData.OpenRaw(path))
            await connection.ExecuteAsync("PRAGMA journal_mode=WAL"); // How earlier versions left their files

        await state.OpenProjectAsync(path);
        await new WbsService(state).AddRootNodeAsync(state.ActiveProject!.Id, "Root");
        File.Copy(path, _dir.File("copy.iscwbs"));
        await state.CloseProjectAsync();

        using var copy = TestData.OpenRaw(_dir.File("copy.iscwbs"));
        Assert.Equal(1, await copy.ScalarAsync<int>("SELECT COUNT(*) FROM WbsNodes"));
    }

    [Fact]
    public async Task OpenProject_MissingFile_IsProjectNotFound()
        => await TestData.AssertFailsWith("Error_ProjectNotFound",
            () => new ProjectStateService(_settings).OpenProjectAsync(_dir.File("missing.iscwbs")));

    [Fact]
    public async Task ExistingProject_ReopensWithItsData()
    {
        string path = _dir.File("project.iscwbs");
        var state = new ProjectStateService(_settings);
        var service = new WbsService(state);
        await state.CreateProjectAsync("Rollout", path, owner: "Carl");
        Guid projectId = state.ActiveProject!.Id;
        WbsNode root = await service.AddRootNodeAsync(projectId, "Root");
        await service.AddChildNodeAsync(root.Id, "Child");
        await state.CloseProjectAsync();

        await state.OpenProjectAsync(path);
        try
        {
            Assert.Equal((projectId, "Rollout", "Carl"), (state.ActiveProject!.Id, state.ActiveProject.Name, state.ActiveProject.Owner));
            Assert.Equal(["1", "1.1"], (await service.GetAllByProjectAsync(projectId)).Select(n => n.Code));
            Assert.Equal(path, _settings.GetRecentProjects()[0]);
        }
        finally
        {
            await state.CloseProjectAsync();
        }
    }

    [Fact]
    public async Task FileFromAnEarlierVersion_GetsTheBaselineColumnsAndNotes_KeepingItsData()
    {
        string path = _dir.File("v2.iscwbs");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sqlite-net-v2.iscwbs"), path);
        var state = new ProjectStateService(_settings);
        var wbs = new WbsService(state);

        await state.OpenProjectAsync(path);
        try
        {
            Assert.Equal(3, await state.Connection.ScalarAsync<int>("SELECT MAX(Version) FROM SchemaVersion"));
            IReadOnlyList<WbsNode> nodes = await wbs.GetAllByProjectAsync(state.ActiveProject!.Id);
            Assert.Equal(3, nodes.Count);
            await wbs.SetBaselineAsync(state.ActiveProject.Id);
            Assert.Equal(new DateTime(2026, 11, 5), (await wbs.GetByIdAsync(nodes[1].Id))!.BaselineDue);
            await wbs.AddNoteAsync(nodes[0].Id, "Upgraded");
        }
        finally
        {
            await state.CloseProjectAsync();
        }
    }

    // The fixture was written by sqlite-net, the data layer before Microsoft.Data.Sqlite.
    [Fact]
    public async Task FileWrittenBySqliteNet_OpensWithAllItsData_AndIsWrittenBackInTheSameFormat()
    {
        string path = _dir.File("legacy.iscwbs");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sqlite-net-v2.iscwbs"), path);
        var state = new ProjectStateService(_settings);
        var wbs = new WbsService(state);
        var milestones = new MilestoneService(state);

        await state.OpenProjectAsync(path);
        try
        {
            Project project = state.ActiveProject!;
            Assert.Equal(
                (Guid.Parse("bf21b785-93e0-4790-a99c-f68137eb0238"), "Fixture", "Owner Å", new DateTime(2026, 10, 29, 6, 56, 43, 669)),
                (project.Id, project.Name, project.Owner, project.CreatedAt));

            IReadOnlyList<WbsNode> nodes = await wbs.GetAllByProjectAsync(project.Id);
            Assert.Equal(["1", "1.1", "1.2"], nodes.Select(n => n.Code));
            Assert.Null(nodes[0].ParentId);
            Assert.Null(nodes[2].DueDate);
            WbsNode design = nodes[1];
            Assert.Equal(
                (Guid.Parse("75e92903-5dad-45ea-844f-320d335cb0d3"), nodes[0].Id, "Design ö", "Line1\nLine2", "Ana", WbsStatus.Complete),
                (design.Id, design.ParentId, design.Title, design.Description, design.AssignedTo, design.Status));
            Assert.Equal(
                (12.5, 3.25, new DateTime(2026, 10, 20), new DateTime(2026, 11, 5), 0, true),
                (design.EstimatedHours, design.ActualHours, design.StartDate, design.DueDate, design.SortOrder, design.IsDeliverable));

            NodeDependency dependency = Assert.Single(await wbs.GetAllDependenciesByProjectAsync(project.Id));
            Assert.Equal((design.Id, nodes[2].Id, DependencyType.StartToFinish), (dependency.PredecessorId, dependency.SuccessorId, dependency.Type));

            Milestone milestone = Assert.Single(await milestones.GetByNodeAsync(design.Id));
            Assert.Equal(("Go-live", new DateTime(2026, 12, 1), false), (milestone.Title, milestone.DueDate, milestone.IsComplete));

            design.Title = "Renamed";
            await wbs.UpdateNodeAsync(design);
            Assert.Equal("2026-11-05T00:00:00.000",
                await state.Connection.ScalarAsync<string>("SELECT DueDate FROM WbsNodes WHERE Title = 'Renamed' AND Id = $1", design.Id));
        }
        finally
        {
            await state.CloseProjectAsync();
        }
    }
}
