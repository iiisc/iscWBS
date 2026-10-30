using Microsoft.Data.Sqlite;
using iscWBS.Core.Exceptions;

namespace iscWBS.Core.Data;

/// <summary>Manages the SQLite connection and schema for a single <c>.iscwbs</c> project file.</summary>
public sealed class WbsDatabase
{
    /// <summary>
    /// Version 2 is the first released layout; 3 adds baseline dates and notes; 4 adds dependency delays and risks.
    /// When the schema changes, bump this and upgrade files whose SchemaVersion row is lower before the app reads them.
    /// </summary>
    private const int _currentSchemaVersion = 4;

    /// <summary>Columns added since version 2. Earlier versions of the app ignore them, so they can still open the file.</summary>
    private static readonly (string Table, string Column, string Definition)[] _addedColumns =
    [
        ("WbsNodes", "BaselineStart", "TEXT"),
        ("WbsNodes", "BaselineDue", "TEXT"),
        // The default fills existing rows, and the rows earlier versions insert.
        ("NodeDependencies", "LagDays", "INTEGER NOT NULL DEFAULT 0"),
    ];

    private const int _appId = 0x49534357; // "ISCW"

    private static readonly string[] _schema =
    [
        "CREATE TABLE IF NOT EXISTS \"SchemaVersion\" (\"Version\" INTEGER NOT NULL PRIMARY KEY, \"AppliedAt\" TEXT NOT NULL DEFAULT '')",
        "CREATE TABLE IF NOT EXISTS \"Projects\" (\"Id\" TEXT NOT NULL PRIMARY KEY, \"Name\" TEXT, \"Description\" TEXT, " +
            "\"Owner\" TEXT, \"CreatedAt\" TEXT, \"UpdatedAt\" TEXT, \"StartDate\" TEXT)",
        "CREATE TABLE IF NOT EXISTS \"WbsNodes\" (\"Id\" TEXT NOT NULL PRIMARY KEY, \"ProjectId\" TEXT, \"ParentId\" TEXT, " +
            "\"Code\" TEXT, \"Title\" TEXT, \"Description\" TEXT, \"AssignedTo\" TEXT, " +
            "\"Status\" INTEGER, \"EstimatedHours\" REAL, \"ActualHours\" REAL, " +
            "\"StartDate\" TEXT, \"DueDate\" TEXT, \"SortOrder\" INTEGER, \"IsDeliverable\" INTEGER, " +
            "\"BaselineStart\" TEXT, \"BaselineDue\" TEXT)",
        "CREATE INDEX IF NOT EXISTS \"WbsNodes_ProjectId\" ON \"WbsNodes\" (\"ProjectId\")",
        "CREATE INDEX IF NOT EXISTS \"WbsNodes_ParentId\" ON \"WbsNodes\" (\"ParentId\")",
        "CREATE TABLE IF NOT EXISTS \"Milestones\" (\"Id\" TEXT NOT NULL PRIMARY KEY, \"ProjectId\" TEXT, \"Title\" TEXT, " +
            "\"DueDate\" TEXT, \"IsComplete\" INTEGER)",
        "CREATE INDEX IF NOT EXISTS \"Milestones_ProjectId\" ON \"Milestones\" (\"ProjectId\")",
        "CREATE TABLE IF NOT EXISTS \"MilestoneNodeLinks\" (\"Id\" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, " +
            "\"MilestoneId\" TEXT, \"NodeId\" TEXT)",
        "CREATE INDEX IF NOT EXISTS \"MilestoneNodeLinks_MilestoneId\" ON \"MilestoneNodeLinks\" (\"MilestoneId\")",
        "CREATE INDEX IF NOT EXISTS \"MilestoneNodeLinks_NodeId\" ON \"MilestoneNodeLinks\" (\"NodeId\")",
        "CREATE TABLE IF NOT EXISTS \"NodeDependencies\" (\"Id\" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, " +
            "\"PredecessorId\" TEXT, \"SuccessorId\" TEXT, \"Type\" INTEGER, \"LagDays\" INTEGER NOT NULL DEFAULT 0)",
        "CREATE INDEX IF NOT EXISTS \"NodeDependencies_PredecessorId\" ON \"NodeDependencies\" (\"PredecessorId\")",
        "CREATE INDEX IF NOT EXISTS \"NodeDependencies_SuccessorId\" ON \"NodeDependencies\" (\"SuccessorId\")",
        "CREATE TABLE IF NOT EXISTS \"NodeNotes\" (\"Id\" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, " +
            "\"NodeId\" TEXT, \"CreatedAt\" TEXT, \"Text\" TEXT)",
        "CREATE INDEX IF NOT EXISTS \"NodeNotes_NodeId\" ON \"NodeNotes\" (\"NodeId\")",
        "CREATE TABLE IF NOT EXISTS \"Risks\" (\"Id\" TEXT NOT NULL PRIMARY KEY, \"ProjectId\" TEXT, \"Title\" TEXT, " +
            "\"Description\" TEXT, \"Likelihood\" INTEGER, \"Impact\" INTEGER, \"Owner\" TEXT, \"Response\" TEXT, " +
            "\"IsClosed\" INTEGER, \"CreatedAt\" TEXT)",
        "CREATE INDEX IF NOT EXISTS \"Risks_ProjectId\" ON \"Risks\" (\"ProjectId\")",
    ];

    // Calling the bundle directly rather than letting Microsoft.Data.Sqlite find it by reflection keeps it AOT-safe.
    static WbsDatabase() => SQLitePCL.Batteries_V2.Init();

    public WbsDatabase(string filePath)
    {
        // Without pooling, closing really releases the file, so it can be moved or deleted straight away.
        Connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = filePath, Pooling = false }.ToString());
    }

    /// <summary>Exposed to the services through <c>ProjectStateService.Connection</c>.</summary>
    public SqliteConnection Connection { get; }

    /// <summary>Checks the file is an iscWBS project, then configures it and creates any missing tables. Safe to call on every open.</summary>
    public async Task InitializeAsync()
    {
        await Connection.OpenAsync();

        // Everything that could reject the file runs before the first write to it.
        int appId;
        try
        {
            appId = await Connection.ScalarAsync<int>("PRAGMA application_id");
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 26) // SQLITE_NOTADB: not an SQLite file at all
        {
            throw NotAProjectFile();
        }
        if (appId == 0)
        {
            // Unstamped is a brand-new file; any existing table means it is someone else's database.
            if (await Connection.ScalarAsync<int>("SELECT COUNT(*) FROM sqlite_master WHERE type='table'") > 0)
                throw NotAProjectFile();
            await Connection.ExecuteAsync($"PRAGMA application_id = {_appId}");
        }
        else if (appId != _appId)
        {
            throw NotAProjectFile();
        }

        // Catches file truncation and page corruption.
        string check = await Connection.ScalarAsync<string>("PRAGMA quick_check") ?? string.Empty;
        if (!string.Equals(check, "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"The project file failed integrity check and may be corrupted: {check}").WithMessageKey("Error_IntegrityCheckFailed", check);

        // WAL keeps recent saves in a side file, which a copy of the project misses and network shares can't share.
        await Connection.ExecuteAsync("PRAGMA journal_mode=DELETE");
        await Connection.ExecuteAsync("PRAGMA foreign_keys=ON");

        foreach (string statement in _schema)
            await Connection.ExecuteAsync(statement);

        foreach ((string table, string column, string definition) in _addedColumns)
        {
            if (await Connection.ScalarAsync<int>($"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = '{column}'") == 0)
                await Connection.ExecuteAsync($"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {definition}");
        }

        if (await Connection.ScalarAsync<int>("SELECT IFNULL(MAX(Version), 0) FROM SchemaVersion") < _currentSchemaVersion)
            await Connection.ExecuteAsync(
                "INSERT INTO SchemaVersion (Version, AppliedAt) VALUES ($1, $2)", _currentSchemaVersion, DateTime.UtcNow.ToString("o"));

        static Exception NotAProjectFile() => new InvalidOperationException(
            "This file was not created by iscWBS and cannot be opened as a project.").WithMessageKey("Error_NotAProjectFile");
    }

    public async Task CloseAsync() => await Connection.DisposeAsync();
}
