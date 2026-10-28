using SQLite;

namespace iscWBS.Core.Data;

public static class SqliteConnectionExtensions
{
    /// <summary>
    /// Runs <paramref name="operation"/> inside an explicit transaction; rolls back and re-throws on failure.
    /// sqlite-net's own <c>RunInTransactionAsync</c> only accepts synchronous work.
    /// </summary>
    public static async Task InTransactionAsync(this SQLiteAsyncConnection connection, Func<Task> operation)
    {
        await connection.ExecuteAsync("BEGIN TRANSACTION");
        try
        {
            await operation();
            await connection.ExecuteAsync("COMMIT");
        }
        catch
        {
            try { await connection.ExecuteAsync("ROLLBACK"); } catch { }
            throw;
        }
    }
}
