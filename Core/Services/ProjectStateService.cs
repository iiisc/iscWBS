using System.IO;
using SQLite;
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

    public SQLiteAsyncConnection Connection => _database?.Connection ?? throw WbsException.NoProjectOpen();

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

        Project? project = await Connection.Table<Project>().FirstOrDefaultAsync();
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
        await Connection.InsertAsync(project);
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
