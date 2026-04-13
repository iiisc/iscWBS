using System.Text.Json;
using SQLite;
using iscWBS.Core.Models;

namespace iscWBS.Core.Repositories;

/// <summary>Manages the SQLite connection and schema for a single <c>.iscwbs</c> project file.</summary>
public sealed class WbsDatabase
{
    /// <summary>Increment this whenever a schema migration is added.</summary>
    private const int _currentSchemaVersion = 2;

    private readonly SQLiteAsyncConnection _connection;

    public WbsDatabase(string filePath)
    {
        _connection = new SQLiteAsyncConnection(filePath, storeDateTimeAsTicks: false);
    }

    /// <summary>The underlying async connection. Passed to repositories via <c>IProjectStateService.Database</c>.</summary>
    public SQLiteAsyncConnection Connection => _connection;

    /// <summary>
    /// Configures pragmas, creates all tables, and applies any pending schema migrations.
    /// Safe to call on every open.
    /// </summary>
    public async Task InitializeAsync()
    {
        // Write-Ahead Logging: better write throughput and crash safety.
        // PRAGMA journal_mode returns a result row, so ExecuteScalarAsync must be used;
        // ExecuteAsync routes through ExecuteNonQuery which throws on SQLITE_ROW.
        await _connection.ExecuteScalarAsync<string>("PRAGMA journal_mode=WAL");
        // Enforce foreign key constraints declared via REFERENCES clauses.
        await _connection.ExecuteAsync("PRAGMA foreign_keys=ON");

        // Verify or stamp the file as an iscWBS project (0x49534357 = "ISCW").
        // Rejects any SQLite file not created by this application.
        int appId = await _connection.ExecuteScalarAsync<int>("PRAGMA application_id");
        if (appId == 0)
        {
            await _connection.ExecuteAsync("PRAGMA application_id = 0x49534357");
        }
        else if (appId != 0x49534357)
        {
            throw new InvalidOperationException(
                "This file was not created by iscWBS and cannot be opened as a project.");
        }

        // Lightweight structural integrity check — catches file truncation and page corruption.
        string check = await _connection.ExecuteScalarAsync<string>("PRAGMA quick_check") ?? string.Empty;
        if (!string.Equals(check, "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"The project file failed integrity check and may be corrupted: {check}");

        // Create tables (no-op when the table already exists).
        //
        // SchemaVersion uses raw SQL instead of CreateTableAsync to avoid a sqlite-net-pcl
        // regression (v1.9.x) where the INTEGER PRIMARY KEY column is mis-identified as
        // missing during table migration and re-added via ALTER TABLE, producing the
        // "duplicate column name: version" SQLite error on existing databases.
        await _connection.ExecuteAsync(
            "CREATE TABLE IF NOT EXISTS \"SchemaVersion\" " +
            "(\"Version\" INTEGER NOT NULL PRIMARY KEY, \"AppliedAt\" TEXT NOT NULL DEFAULT '')");

        await _connection.CreateTableAsync<Project>();

        // Pre-migrate WbsNodes.IsDeliverable before CreateTableAsync runs.
        // sqlite-net-pcl generates ALTER TABLE … ADD COLUMN without a DEFAULT clause,
        // which SQLite rejects for NOT NULL columns on non-empty tables.
        // Running the explicit migration first (with DEFAULT 0) prevents that failure.
        if (await TableExistsAsync("WbsNodes") && !await HasColumnAsync("WbsNodes", "IsDeliverable"))
            await _connection.ExecuteAsync(
                "ALTER TABLE \"WbsNodes\" ADD COLUMN \"IsDeliverable\" INTEGER NOT NULL DEFAULT 0");

        await _connection.CreateTableAsync<WbsNode>();
        await _connection.CreateTableAsync<Milestone>();

        // Use raw SQL for tables with INTEGER PRIMARY KEY AUTOINCREMENT to work around the
        // same sqlite-net-pcl v1.9.x regression as SchemaVersion: the INTEGER PRIMARY KEY
        // column is mis-identified as missing on existing tables and re-added via ALTER TABLE,
        // producing a "duplicate column name: Id" SQLite error when opening existing projects.
        await _connection.ExecuteAsync(
            "CREATE TABLE IF NOT EXISTS \"MilestoneNodeLinks\" " +
            "(\"Id\" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, " +
            "\"MilestoneId\" TEXT, \"NodeId\" TEXT)");
        await _connection.ExecuteAsync(
            "CREATE INDEX IF NOT EXISTS \"MilestoneNodeLinks_MilestoneId\" " +
            "ON \"MilestoneNodeLinks\" (\"MilestoneId\")");
        await _connection.ExecuteAsync(
            "CREATE INDEX IF NOT EXISTS \"MilestoneNodeLinks_NodeId\" " +
            "ON \"MilestoneNodeLinks\" (\"NodeId\")");

        await _connection.ExecuteAsync(
            "CREATE TABLE IF NOT EXISTS \"NodeDependencies\" " +
            "(\"Id\" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, " +
            "\"PredecessorId\" TEXT, \"SuccessorId\" TEXT, \"Type\" INTEGER)");
        await _connection.ExecuteAsync(
            "CREATE INDEX IF NOT EXISTS \"NodeDependencies_PredecessorId\" " +
            "ON \"NodeDependencies\" (\"PredecessorId\")");
        await _connection.ExecuteAsync(
            "CREATE INDEX IF NOT EXISTS \"NodeDependencies_SuccessorId\" " +
            "ON \"NodeDependencies\" (\"SuccessorId\")");


        SchemaVersion? row = await _connection.Table<SchemaVersion>().FirstOrDefaultAsync();
        if (row is null)
        {
            // Either a brand-new file or a pre-versioned legacy file.
            if (await HasColumnAsync("Milestones", "LinkedNodeIds"))
            {
                await _connection.ExecuteAsync("BEGIN TRANSACTION");
                try
                {
                    await MigrateTo1Async();
                    await _connection.ExecuteAsync("COMMIT");
                }
                catch
                {
                    try { await _connection.ExecuteAsync("ROLLBACK"); } catch { }
                    throw;
                }
            }

            await _connection.InsertAsync(new SchemaVersion { Version = _currentSchemaVersion });
        }
        else if (row.Version < _currentSchemaVersion)
        {
            await ApplyMigrationsAsync(row.Version);
            row.Version = _currentSchemaVersion;
            row.AppliedAt = DateTime.UtcNow.ToString("o");
            await _connection.UpdateAsync(row);
        }
    }

    /// <summary>Runs every migration between <paramref name="fromVersion"/> and <see cref="_currentSchemaVersion"/>.</summary>
    private async Task ApplyMigrationsAsync(int fromVersion)
    {
        await _connection.ExecuteAsync("BEGIN TRANSACTION");
        try
        {
            if (fromVersion < 1)
                await MigrateTo1Async();

            if (fromVersion < 2)
                await MigrateTo2Async();

            await _connection.ExecuteAsync("COMMIT");
        }
        catch
        {
            try { await _connection.ExecuteAsync("ROLLBACK"); } catch { }
            throw;
        }
    }

    /// <summary>Returns <see langword="true"/> when a table named <paramref name="table"/> exists in the database.</summary>
    private async Task<bool> TableExistsAsync(string table)
    {
        int count = await _connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=?", table);
        return count > 0;
    }

    /// <summary>Returns <see langword="true"/> when <paramref name="table"/> has a column named <paramref name="column"/>.</summary>
    private async Task<bool> HasColumnAsync(string table, string column)
    {
        try
        {
            // Note: PRAGMA table-valued functions do not support parameter binding for the table
            // name argument. The table name is embedded as a literal; column name uses a parameter.
            int count = await _connection.ExecuteScalarAsync<int>(
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
        List<LegacyMilestoneLinkRow> rows = await _connection.QueryAsync<LegacyMilestoneLinkRow>(
            "SELECT Id, LinkedNodeIds FROM Milestones " +
            "WHERE LinkedNodeIds IS NOT NULL AND LinkedNodeIds != '[]'");

        foreach (LegacyMilestoneLinkRow legacy in rows)
        {
            if (string.IsNullOrWhiteSpace(legacy.LinkedNodeIds))
                continue;

            try
            {
                List<string>? ids = JsonSerializer.Deserialize<List<string>>(legacy.LinkedNodeIds);
                if (ids is null) continue;

                foreach (string idStr in ids)
                {
                    if (Guid.TryParse(idStr, out Guid nodeId))
                    {
                        await _connection.InsertAsync(new MilestoneNodeLink
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

    public async Task CloseAsync() => await _connection.CloseAsync();

    /// <summary>
    /// Migration v1 → v2: adds the <c>IsDeliverable</c> flag column to <c>WbsNodes</c>.
    /// All existing nodes default to <see langword="false"/> (not a deliverable).
    /// Guarded by <see cref="HasColumnAsync"/> because sqlite-net-pcl's
    /// <c>CreateTableAsync</c> may have already added the column via its own auto-migration.
    /// </summary>
    private async Task MigrateTo2Async()
    {
        if (!await HasColumnAsync("WbsNodes", "IsDeliverable"))
            await _connection.ExecuteAsync(
                "ALTER TABLE WbsNodes ADD COLUMN IsDeliverable INTEGER NOT NULL DEFAULT 0");
    }

    // Used only by MigrateTo1Async; not part of the live schema.
    private class LegacyMilestoneLinkRow
    {
        public Guid Id { get; set; }
        public string LinkedNodeIds { get; set; } = string.Empty;
    }
}

