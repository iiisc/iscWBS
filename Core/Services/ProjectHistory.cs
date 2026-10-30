using Microsoft.Data.Sqlite;
using iscWBS.Core.Data;

namespace iscWBS.Core.Services;

/// <summary>
/// Undo and redo for the open project. Every change first records a copy of the project's tables, and undoing
/// puts that copy back. Projects are small enough for this to be quick, and unlike a hand-written reverse
/// operation per edit, a copy can't drift from what the edit actually did.
/// </summary>
public sealed class ProjectHistory(Func<SqliteConnection> connection)
{
    public const int Depth = 50;

    private static readonly string[] _tables =
        ["Projects", "WbsNodes", "NodeDependencies", "Milestones", "MilestoneNodeLinks", "NodeNotes"];

    private readonly LinkedList<Snapshot> _undo = new();
    private readonly Stack<Snapshot> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public event EventHandler? Changed;

    /// <summary>
    /// Runs <paramref name="change"/> in one transaction as an undoable step. A change that fails is rolled back
    /// and leaves no step behind.
    /// </summary>
    public async Task ChangeAsync(Func<Task> change)
    {
        Snapshot before = await TakeAsync();
        await connection().InTransactionAsync(change);
        _undo.AddLast(before);
        if (_undo.Count > Depth)
            _undo.RemoveFirst();
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<bool> UndoAsync()
    {
        if (_undo.Last is not { } last) return false;
        _redo.Push(await TakeAsync());
        _undo.RemoveLast();
        await RestoreAsync(last.Value);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public async Task<bool> RedoAsync()
    {
        if (!_redo.TryPop(out Snapshot? next)) return false;
        _undo.AddLast(await TakeAsync());
        await RestoreAsync(next);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Forgets every step, for when another project is opened.</summary>
    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed record Table(string Name, string[] Columns, List<object[]> Rows);

    private sealed record Snapshot(List<Table> Tables);

    private async Task<Snapshot> TakeAsync()
    {
        var tables = new List<Table>();
        foreach (string name in _tables)
        {
            await using SqliteCommand command = connection().CreateCommand();
            command.CommandText = $"SELECT * FROM \"{name}\"";
            await using SqliteDataReader reader = await command.ExecuteReaderAsync();
            string[] columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
            var rows = new List<object[]>();
            while (await reader.ReadAsync())
            {
                var values = new object[reader.FieldCount];
                reader.GetValues(values);
                rows.Add(values);
            }
            tables.Add(new Table(name, columns, rows));
        }
        return new Snapshot(tables);
    }

    private async Task RestoreAsync(Snapshot snapshot)
    {
        SqliteConnection db = connection();
        await db.InTransactionAsync(async () =>
        {
            foreach (Table table in snapshot.Tables)
            {
                await db.ExecuteAsync($"DELETE FROM \"{table.Name}\"");
                string columns = string.Join(", ", table.Columns.Select(c => $"\"{c}\""));
                string values = string.Join(", ", table.Columns.Select((_, i) => $"${i + 1}"));
                foreach (object[] row in table.Rows)
                    await db.ExecuteAsync($"INSERT INTO \"{table.Name}\" ({columns}) VALUES ({values})", row);
            }
        });
    }
}
