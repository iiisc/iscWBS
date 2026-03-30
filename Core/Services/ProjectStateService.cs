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

    public async Task OpenProjectAsync(string filePath)
    {
        if (!File.Exists(filePath))
            throw new ProjectNotFoundException(filePath);

        await CloseProjectAsync();

        _database = new WbsDatabase(filePath);
        await _database.InitializeAsync();

        var repo = new ProjectRepository(this);
        _activeProject = await repo.GetFirstAsync()
            ?? throw new WbsException($"'{filePath}' does not contain valid project data.");

        ActiveProjectChanged?.Invoke(this, _activeProject);
    }

    public async Task CreateProjectAsync(string name, string filePath, string owner = "", string currency = "USD")
    {
        await CloseProjectAsync();

        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        _database = new WbsDatabase(filePath);
        await _database.InitializeAsync();

        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = name,
            Owner = owner,
            Currency = string.IsNullOrWhiteSpace(currency) ? "USD" : currency,
            CreatedAt = DateTime.UtcNow
        };

        var repo = new ProjectRepository(this);
        await repo.InsertAsync(project);
        _activeProject = project;

        ActiveProjectChanged?.Invoke(this, _activeProject);
    }

    public async Task CloseProjectAsync()
    {
        if (_database is not null)
        {
            await _database.CloseAsync();
            _database = null;
        }

        _activeProject = null;
        ActiveProjectChanged?.Invoke(this, null);
    }
}
