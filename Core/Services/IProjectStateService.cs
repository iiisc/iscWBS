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

    /// <summary>Opens an existing <c>.iscwbs</c> project file.</summary>
    Task OpenProjectAsync(string filePath);

    /// <summary>Creates a new project at the specified file path and opens it.</summary>
    Task CreateProjectAsync(string name, string filePath, string owner = "", string currency = "USD");

    /// <summary>Closes the current project and disposes the database connection.</summary>
    Task CloseProjectAsync();
}
