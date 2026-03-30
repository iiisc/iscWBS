using SQLite;
using iscWBS.Core.Models;

namespace iscWBS.Core.Repositories;

/// <summary>Manages the SQLite connection and schema for a single <c>.iscwbs</c> project file.</summary>
public sealed class WbsDatabase
{
    private readonly SQLiteAsyncConnection _connection;

    public WbsDatabase(string filePath)
    {
        _connection = new SQLiteAsyncConnection(filePath, storeDateTimeAsTicks: false);
    }

    /// <summary>The underlying async connection. Passed to repositories via <c>IProjectStateService.Database</c>.</summary>
    public SQLiteAsyncConnection Connection => _connection;

    /// <summary>Creates all tables if they do not already exist. Safe to call on every open.</summary>
    public async Task InitializeAsync()
    {
        await _connection.CreateTableAsync<SchemaVersion>();
        await _connection.CreateTableAsync<Project>();
        await _connection.CreateTableAsync<WbsNode>();
        await _connection.CreateTableAsync<Milestone>();
        await _connection.CreateTableAsync<NodeDependency>();
    }

    public async Task CloseAsync() => await _connection.CloseAsync();
}
