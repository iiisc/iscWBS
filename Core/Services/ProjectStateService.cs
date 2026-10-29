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
    private WbsDatabase? _database;
    private Project? _activeProject;

    public Project? ActiveProject => _activeProject;
    public bool HasActiveProject => _activeProject is not null;

    public SqliteConnection Connection => _database?.Connection ?? throw WbsException.NoProjectOpen();

    public event EventHandler<Project?>? ActiveProjectChanged;

    public ProjectStateService(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public async Task OpenProjectAsync(string filePath)
    {
        if (!File.Exists(filePath))
            throw WbsException.ProjectNotFound(filePath);

        await OpenDatabaseAsync(filePath);

        Project? project = (await Connection.QueryAsync(
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
            })).FirstOrDefault();
        if (project is null)
        {
            await CloseInternalAsync();
            throw new WbsException($"'{filePath}' does not contain valid project data.").WithMessageKey("Error_InvalidProjectData", filePath);
        }
        _activeProject = project;

        _settingsService.AddRecentProject(filePath);
        ActiveProjectChanged?.Invoke(this, _activeProject);
    }

    public async Task CreateProjectAsync(string name, string filePath, string owner = "")
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await OpenDatabaseAsync(filePath);

        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = name,
            Owner = owner,
            CreatedAt = DateTime.UtcNow
        };
        await Connection.ExecuteAsync(
            "INSERT INTO Projects (Id, Name, Description, Owner, CreatedAt, UpdatedAt, StartDate) VALUES ($1, $2, $3, $4, $5, $6, $7)",
            project.Id, project.Name, project.Description, project.Owner, project.CreatedAt, project.UpdatedAt, project.StartDate);
        _activeProject = project;

        _settingsService.AddRecentProject(filePath);
        if (!string.IsNullOrEmpty(directory))
            _settingsService.Set(SettingsKeys.LastProjectFolder, directory);
        ActiveProjectChanged?.Invoke(this, _activeProject);
    }

    public async Task CloseProjectAsync()
    {
        await CloseInternalAsync();
        ActiveProjectChanged?.Invoke(this, null);
    }

    private async Task OpenDatabaseAsync(string filePath)
    {
        await CloseInternalAsync();

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
        _database = database;
    }

    private async Task CloseInternalAsync()
    {
        _activeProject = null;
        if (_database is null) return;

        WbsDatabase database = _database;
        _database = null;
        await database.CloseAsync();
    }
}
