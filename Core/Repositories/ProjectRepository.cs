using SQLite;
using iscWBS.Core.Exceptions;
using iscWBS.Core.Models;
using iscWBS.Core.Services;

namespace iscWBS.Core.Repositories;

public sealed class ProjectRepository
{
    private SQLiteAsyncConnection Connection =>
        _projectStateService.Database
            ?? throw new WbsException("No project is currently open.");

    private readonly IProjectStateService _projectStateService;

    public ProjectRepository(IProjectStateService projectStateService)
    {
        _projectStateService = projectStateService;
    }

    public async Task<Project?> GetFirstAsync()
        => await Connection.Table<Project>().FirstOrDefaultAsync();

    public async Task<Project?> GetByIdAsync(Guid id)
        => await Connection.Table<Project>()
            .Where(p => p.Id == id)
            .FirstOrDefaultAsync();

    public async Task InsertAsync(Project project)
        => await Connection.InsertAsync(project);

    public async Task UpdateAsync(Project project)
        => await Connection.UpdateAsync(project);
}
