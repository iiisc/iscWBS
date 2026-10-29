using System.Globalization;
using Microsoft.Data.Sqlite;

namespace iscWBS.Core.Data;

/// <summary>
/// Query helpers over Microsoft.Data.Sqlite. Parameters are positional (<c>$1</c>, <c>$2</c>, …) and are
/// stored the way sqlite-net wrote them, so existing project files stay readable: GUIDs as lowercase text
/// (Microsoft.Data.Sqlite would write blobs) and dates as <see cref="DateFormat"/> text.
/// </summary>
public static class SqliteExtensions
{
    public const string DateFormat = "yyyy'-'MM'-'dd'T'HH':'mm':'ss.fff";

    /// <summary>Returns the number of rows changed.</summary>
    public static async Task<int> ExecuteAsync(this SqliteConnection connection, string sql, params object?[] args)
    {
        await using SqliteCommand command = CreateCommand(connection, sql, args);
        return await command.ExecuteNonQueryAsync();
    }

    public static async Task<List<T>> QueryAsync<T>(
        this SqliteConnection connection, string sql, Func<SqliteDataReader, T> read, params object?[] args)
    {
        await using SqliteCommand command = CreateCommand(connection, sql, args);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        var rows = new List<T>();
        while (await reader.ReadAsync())
            rows.Add(read(reader));
        return rows;
    }

    /// <summary>The first column of the first row, or <c>default</c> when there is none.</summary>
    public static async Task<T?> ScalarAsync<T>(this SqliteConnection connection, string sql, params object?[] args)
    {
        await using SqliteCommand command = CreateCommand(connection, sql, args);
        object? value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? default : (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
    }

    /// <summary>Runs <paramref name="operation"/> in a transaction; rolls back and re-throws on failure.</summary>
    public static async Task InTransactionAsync(this SqliteConnection connection, Func<Task> operation)
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

    /// <summary>Text, with NULL read as empty like the models' defaults.</summary>
    public static string GetText(this SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);

    public static Guid GetGuidText(this SqliteDataReader reader, int ordinal) => Guid.Parse(reader.GetString(ordinal));

    public static Guid? GetNullableGuid(this SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : Guid.Parse(reader.GetString(ordinal));

    public static DateTime? GetNullableDateTime(this SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);

    private static SqliteCommand CreateCommand(SqliteConnection connection, string sql, object?[] args)
    {
        SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        for (int i = 0; i < args.Length; i++)
        {
            command.Parameters.AddWithValue($"${i + 1}", args[i] switch
            {
                null => DBNull.Value,
                Guid guid => guid.ToString(),
                DateTime date => date.ToString(DateFormat, CultureInfo.InvariantCulture),
                Enum value => Convert.ToInt64(value, CultureInfo.InvariantCulture),
                var value => value
            });
        }
        return command;
    }
}
