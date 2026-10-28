using iscWBS.Core.Data;
using iscWBS.Core.Models;
using iscWBS.Core.Services;
using SQLite;

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
        using var connection = new SQLiteConnection(path);
        connection.Execute($"PRAGMA application_id = {applicationId}");
        connection.Execute("CREATE TABLE Things (Id INTEGER PRIMARY KEY)");
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
        using (var connection = new SQLiteConnection(path))
        {
            Assert.Equal(0, connection.ExecuteScalar<int>("PRAGMA application_id"));
            Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE name = 'Projects'"));
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
        using (var connection = new SQLiteConnection(path))
            connection.Execute("DELETE FROM Projects");

        await TestData.AssertFailsWith("Error_InvalidProjectData", () => state.OpenProjectAsync(path));

        Assert.False(state.HasActiveProject);
        File.Delete(path);
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
}
