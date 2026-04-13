using System.IO;
using SQLite;
using iscWBS.Core.Exceptions;
using iscWBS.Core.Models;
using iscWBS.Core.Repositories;

namespace iscWBS.Core.Services;

public sealed class ProjectStateService : IProjectStateService
{
    private WbsDatabase? _database;
    private Project? _activeProject;

    public Project? ActiveProject => _activeProject;
    public bool HasActiveProject => _activeProject is not null;
    public SQLiteAsyncConnection? Database => _database?.Connection;

    public event EventHandler<Project?>? ActiveProjectChanged;
    public event EventHandler<Project>? ActiveProjectUpdated;

    public async Task OpenProjectAsync(string filePath)
    {
        if (!File.Exists(filePath))
            throw new ProjectNotFoundException(filePath);

        await CloseInternalAsync();

        _database = new WbsDatabase(filePath);
        try
        {
            await _database.InitializeAsync();
        }
        catch
        {
            await _database.CloseAsync();
            _database = null;
            throw;
        }

        var repo = new ProjectRepository(this);
        _activeProject = await repo.GetFirstAsync()
            ?? throw new WbsException($"'{filePath}' does not contain valid project data.");

        ActiveProjectChanged?.Invoke(this, _activeProject);
    }

    public async Task CreateProjectAsync(string name, string filePath, string owner = "")
    {
        await CloseInternalAsync();

        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        _database = new WbsDatabase(filePath);
        try
        {
            await _database.InitializeAsync();
        }
        catch
        {
            await _database.CloseAsync();
            _database = null;
            throw;
        }

        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = name,
            Owner = owner,
            CreatedAt = DateTime.UtcNow
        };

        var repo = new ProjectRepository(this);
        await repo.InsertAsync(project);
        _activeProject = project;

        ActiveProjectChanged?.Invoke(this, _activeProject);
    }

    public async Task CloseProjectAsync()
    {
        await CloseInternalAsync();
        ActiveProjectChanged?.Invoke(this, null);
    }

    private async Task CloseInternalAsync()
    {
        if (_database is not null)
        {
            try
            {
                await _database.CloseAsync();
            }
            finally
            {
                _database = null;
            }
        }

        _activeProject = null;
    }

    public async Task UpdateProjectAsync(Project project)
    {
        if (_activeProject is null)
            throw new WbsException("No project is currently open.");

        project.UpdatedAt = DateTime.UtcNow;
        var repo = new ProjectRepository(this);
        await repo.UpdateAsync(project);
        _activeProject = project;
        ActiveProjectUpdated?.Invoke(this, project);
    }
}
