using System.Text.Json;
using SQLite;
using iscWBS.Core.Exceptions;
using iscWBS.Core.Models;

namespace iscWBS.Core.Data;

/// <summary>Manages the SQLite connection and schema for a single <c>.iscwbs</c> project file.</summary>
public sealed class WbsDatabase
{
    /// <summary>Increment this whenever a schema migration is added.</summary>
    private const int _currentSchemaVersion = 2;

    private const int _appId = 0x49534357; // "ISCW"

    // All tables use raw SQL (CREATE TABLE IF NOT EXISTS) instead of CreateTableAsync<T> for two reasons:
    //   1. sqlite-net-pcl v1.9.x regression: INTEGER PRIMARY KEY columns are mis-identified as missing and
    //      re-added via ALTER TABLE, producing a "duplicate column name" error on existing databases.
    //   2. PublishTrimmed=True (Release) can strip the attribute metadata CreateTableAsync relies on for its
    //      column-migration logic, causing it to treat every column as missing and trigger the same error.
    private static readonly string[] _schema =
    [
        "CREATE TABLE IF NOT EXISTS \"SchemaVersion\" (\"Version\" INTEGER NOT NULL PRIMARY KEY, \"AppliedAt\" TEXT NOT NULL DEFAULT '')",
        "CREATE TABLE IF NOT EXISTS \"Projects\" (\"Id\" TEXT NOT NULL PRIMARY KEY, \"Name\" TEXT, \"Description\" TEXT, " +
            "\"Owner\" TEXT, \"CreatedAt\" TEXT, \"UpdatedAt\" TEXT, \"StartDate\" TEXT)",
        "CREATE TABLE IF NOT EXISTS \"WbsNodes\" (\"Id\" TEXT NOT NULL PRIMARY KEY, \"ProjectId\" TEXT, \"ParentId\" TEXT, " +
            "\"Code\" TEXT, \"Title\" TEXT, \"Description\" TEXT, \"AssignedTo\" TEXT, " +
            "\"Status\" INTEGER, \"EstimatedHours\" REAL, \"ActualHours\" REAL, " +
            "\"StartDate\" TEXT, \"DueDate\" TEXT, \"SortOrder\" INTEGER, \"IsDeliverable\" INTEGER)",
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
            "\"PredecessorId\" TEXT, \"SuccessorId\" TEXT, \"Type\" INTEGER)",
        "CREATE INDEX IF NOT EXISTS \"NodeDependencies_PredecessorId\" ON \"NodeDependencies\" (\"PredecessorId\")",
        "CREATE INDEX IF NOT EXISTS \"NodeDependencies_SuccessorId\" ON \"NodeDependencies\" (\"SuccessorId\")",
    ];

    public WbsDatabase(string filePath)
    {
        Connection = new SQLiteAsyncConnection(filePath, storeDateTimeAsTicks: false);
    }

    /// <summary>Exposed to the services through <c>ProjectStateService.Connection</c>.</summary>
    public SQLiteAsyncConnection Connection { get; }

    /// <summary>Configures pragmas, creates all tables, and applies any pending schema migrations. Safe to call on every open.</summary>
    public async Task InitializeAsync()
    {
        // PRAGMA journal_mode returns a result row, so ExecuteScalarAsync must be used;
        // ExecuteAsync routes through ExecuteNonQuery which throws on SQLITE_ROW.
        await Connection.ExecuteScalarAsync<string>("PRAGMA journal_mode=WAL");
        await Connection.ExecuteAsync("PRAGMA foreign_keys=ON");

        int appId = await Connection.ExecuteScalarAsync<int>("PRAGMA application_id");
        if (appId == 0)
        {
            // Unstamped is either a brand-new file or a project from before stamping existed; anything
            // else is someone's unrelated database and must be rejected before we write to it.
            int tableCount = await Connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table'");
            if (tableCount > 0 && !await TableExistsAsync("Projects"))
                throw NotAProjectFile();
            await Connection.ExecuteAsync($"PRAGMA application_id = {_appId}");
        }
        else if (appId != _appId)
        {
            throw NotAProjectFile();
        }

        // Catches file truncation and page corruption.
        string check = await Connection.ExecuteScalarAsync<string>("PRAGMA quick_check") ?? string.Empty;
        if (!string.Equals(check, "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"The project file failed integrity check and may be corrupted: {check}").WithMessageKey("Error_IntegrityCheckFailed", check);

        // On files from before v2 the column must exist before CREATE TABLE IF NOT EXISTS turns into a no-op.
        if (await TableExistsAsync("WbsNodes"))
            await MigrateTo2Async();

        foreach (string statement in _schema)
            await Connection.ExecuteAsync(statement);

        SchemaVersion? row = await Connection.Table<SchemaVersion>().FirstOrDefaultAsync();
        if (row is null)
        {
            // Either a brand-new file or a pre-versioned legacy file.
            if (await HasColumnAsync("Milestones", "LinkedNodeIds"))
                await Connection.InTransactionAsync(MigrateTo1Async);

            await Connection.InsertAsync(new SchemaVersion { Version = _currentSchemaVersion });
        }
        else if (row.Version < _currentSchemaVersion)
        {
            // v2 already ran above, ahead of table creation.
            if (row.Version < 1)
                await Connection.InTransactionAsync(MigrateTo1Async);
            row.Version = _currentSchemaVersion;
            row.AppliedAt = DateTime.UtcNow.ToString("o");
            await Connection.UpdateAsync(row);
        }

        static Exception NotAProjectFile() => new InvalidOperationException(
            "This file was not created by iscWBS and cannot be opened as a project.").WithMessageKey("Error_NotAProjectFile");
    }

    public async Task CloseAsync() => await Connection.CloseAsync();

    private async Task<bool> TableExistsAsync(string table)
        => await Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=?", table) > 0;

    private async Task<bool> HasColumnAsync(string table, string column)
    {
        try
        {
            // PRAGMA table-valued functions can't bind the table name, so it is embedded as a literal.
            int count = await Connection.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = ?",
                column);
            return count > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Migration v0 → v1: moves linked-node data from the legacy <c>LinkedNodeIds</c>
    /// JSON column on <c>Milestones</c> into the new <c>MilestoneNodeLinks</c> junction table.
    /// </summary>
    private async Task MigrateTo1Async()
    {
        List<LegacyMilestoneLinkRow> rows = await Connection.QueryAsync<LegacyMilestoneLinkRow>(
            "SELECT Id, LinkedNodeIds FROM Milestones " +
            "WHERE LinkedNodeIds IS NOT NULL AND LinkedNodeIds != '[]'");

        foreach (LegacyMilestoneLinkRow legacy in rows)
        {
            if (string.IsNullOrWhiteSpace(legacy.LinkedNodeIds))
                continue;

            try
            {
                List<string>? ids = JsonSerializer.Deserialize(legacy.LinkedNodeIds, AppJsonContext.Default.ListString);
                if (ids is null) continue;

                foreach (string idStr in ids)
                {
                    if (Guid.TryParse(idStr, out Guid nodeId))
                    {
                        await Connection.InsertAsync(new MilestoneNodeLink
                        {
                            MilestoneId = legacy.Id,
                            NodeId = nodeId
                        });
                    }
                }
            }
            catch { }
        }
    }

    /// <summary>Migration v1 → v2: adds <c>WbsNodes.IsDeliverable</c>, defaulting to false. Idempotent.</summary>
    private async Task MigrateTo2Async()
    {
        if (!await HasColumnAsync("WbsNodes", "IsDeliverable"))
            await Connection.ExecuteAsync(
                "ALTER TABLE WbsNodes ADD COLUMN IsDeliverable INTEGER NOT NULL DEFAULT 0");
    }

    // Used only by MigrateTo1Async; not part of the live schema.
    private class LegacyMilestoneLinkRow
    {
        public Guid Id { get; set; }
        public string LinkedNodeIds { get; set; } = string.Empty;
    }
}

