using SQLite;
using iscWBS.Core.Models;

namespace iscWBS.Core.Services;

/// <summary>Owns the active project and its SQLite connection. All repositories draw their connection from here.</summary>
public interface IProjectStateService
{
    /// <summary>The currently open project, or <see langword="null"/> if no project is open.</summary>
    Project? ActiveProject { get; }

    /// <summary>Whether a project is currently open.</summary>
    bool HasActiveProject { get; }

    /// <summary>The active SQLite connection, or <see langword="null"/> if no project is open.</summary>
    SQLiteAsyncConnection? Database { get; }

    /// <summary>Fires whenever the active project is opened, closed, or switched.</summary>
    event EventHandler<Project?> ActiveProjectChanged;

    /// <summary>Fires when the active project's metadata is updated without switching projects.</summary>
    event EventHandler<Project> ActiveProjectUpdated;

    /// <summary>Opens an existing <c>.iscwbs</c> project file.</summary>
    Task OpenProjectAsync(string filePath);

    /// <summary>Creates a new project at the specified file path and opens it.</summary>
    Task CreateProjectAsync(string name, string filePath, string owner = "");

    /// <summary>Closes the current project and disposes the database connection.</summary>
    Task CloseProjectAsync();

    /// <summary>
    /// Persists metadata changes to the active project and fires <see cref="ActiveProjectUpdated"/>.
    /// Sets <see cref="Project.UpdatedAt"/> to the current UTC time automatically.
    /// </summary>
    Task UpdateProjectAsync(Project project);
}
