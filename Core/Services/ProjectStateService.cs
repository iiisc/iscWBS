using System.IO;
using Microsoft.Data.Sqlite;
using iscWBS.Core.Exceptions;
using iscWBS.Core.Models;
using iscWBS.Core.Data;
using iscWBS.Helpers;

namespace iscWBS.Core.Services;

/// <summary>Owns the open project file; every service reads and writes through <see cref="Connection"/>.</summary>
public sealed class ProjectStateService
{
    private readonly SettingsService _settingsService;
    private readonly BackupService? _backups;
    private WbsDatabase? _database;
    private string? _filePath;
    private Project? _activeProject;

    public Project? ActiveProject => _activeProject;
    public bool HasActiveProject => _activeProject is not null;

    public SqliteConnection Connection => _database?.Connection ?? throw WbsException.NoProjectOpen();

    /// <summary>Undo and redo for the open project; emptied whenever another project opens.</summary>
    public ProjectHistory History { get; }

    public event EventHandler<Project?>? ActiveProjectChanged;

    public ProjectStateService(SettingsService settingsService, BackupService? backups = null)
    {
        _settingsService = settingsService;
        _backups = backups;
        History = new ProjectHistory(() => Connection);
    }

    public async Task OpenProjectAsync(string filePath)
    {
        if (!File.Exists(filePath))
            throw WbsException.ProjectNotFound(filePath);

        WbsDatabase database = await OpenDatabaseAsync(filePath);
        Project? project;
        try
        {
            project = (await database.Connection.QueryAsync(
                "SELECT Id, Name, Description, Owner, CreatedAt, UpdatedAt, StartDate FROM Projects LIMIT 1",
                r => new Project
                {
                    Id = r.GetGuidText(0),
                    Name = r.GetText(1),
                    Description = r.GetText(2),
                    Owner = r.GetText(3),
                    CreatedAt = r.GetNullableDateTime(4) ?? default,
                    UpdatedAt = r.GetNullableDateTime(5),
                    StartDate = r.GetNullableDateTime(6)
                })).FirstOrDefault()
                ?? throw new WbsException($"'{filePath}' does not contain valid project data.").WithMessageKey("Error_InvalidProjectData", filePath);
        }
        catch
        {
            await database.CloseAsync();
            throw;
        }
        await ActivateAsync(database, project, filePath);
    }

    public async Task CreateProjectAsync(string name, string filePath, string owner = "")
    {
        // The file would open as the existing project, and a second project in it is never shown again.
        if (File.Exists(filePath))
            throw new WbsException($"'{filePath}' already exists.").WithMessageKey("Error_ProjectFileExists", filePath);

        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        WbsDatabase database = await OpenDatabaseAsync(filePath);
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = name,
            Owner = owner,
            CreatedAt = DateTime.UtcNow
        };
        try
        {
            await database.Connection.ExecuteAsync(
                "INSERT INTO Projects (Id, Name, Description, Owner, CreatedAt, UpdatedAt, StartDate) VALUES ($1, $2, $3, $4, $5, $6, $7)",
                project.Id, project.Name, project.Description, project.Owner, project.CreatedAt, project.UpdatedAt, project.StartDate);
        }
        catch
        {
            await database.CloseAsync();
            throw;
        }
        await ActivateAsync(database, project, filePath);
        if (!string.IsNullOrEmpty(directory))
            _settingsService.Set(SettingsKeys.LastProjectFolder, directory);
    }

    public async Task CloseProjectAsync()
    {
        await CloseInternalAsync();
        ActiveProjectChanged?.Invoke(this, null);
    }

    /// <summary>Opens and checks the file without touching the open project, which stays open if this fails.</summary>
    private static async Task<WbsDatabase> OpenDatabaseAsync(string filePath)
    {
        var database = new WbsDatabase(filePath);
        try
        {
            await database.InitializeAsync();
        }
        catch
        {
            await database.CloseAsync();
            throw;
        }
        return database;
    }

    private async Task ActivateAsync(WbsDatabase database, Project project, string filePath)
    {
        await CloseInternalAsync();
        _database = database;
        _activeProject = project;
        _filePath = filePath;
        History.Clear();
        _backups?.BackUp(filePath);
        _settingsService.AddRecentProject(filePath);
        ActiveProjectChanged?.Invoke(this, project);
    }

    private async Task CloseInternalAsync()
    {
        _activeProject = null;
        History.Clear();
        if (_database is null) return;

        WbsDatabase database = _database;
        _database = null;
        await database.CloseAsync();
        _backups?.BackUp(_filePath!);
    }

    /// <summary>Closes the project and takes its closing backup without waiting, for when the app is exiting.</summary>
    public void CloseOnExit()
    {
        if (_database is null) return;
        _database.Connection.Dispose();
        _database = null;
        _backups?.BackUp(_filePath!);
    }
}
