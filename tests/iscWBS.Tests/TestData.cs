using Microsoft.Data.Sqlite;
using iscWBS.Core.Exceptions;
using iscWBS.Core.Models;

namespace iscWBS.Tests;

internal static class TestData
{
    public static NodeDependency Dep(WbsNode pred, WbsNode succ, DependencyType type = DependencyType.FinishToStart)
        => new() { PredecessorId = pred.Id, SuccessorId = succ.Id, Type = type };

    public static WbsNode Node(WbsStatus status = WbsStatus.NotStarted, WbsNode? parent = null, int sortOrder = 0)
        => new() { Status = status, ParentId = parent?.Id, SortOrder = sortOrder };

    /// <summary>A raw connection for arranging or inspecting a file outside the services.</summary>
    public static SqliteConnection OpenRaw(string path)
    {
        SQLitePCL.Batteries_V2.Init();
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    public static async Task AssertFailsWith(string messageKey, Func<Task> action)
    {
        WbsException ex = await Assert.ThrowsAsync<WbsException>(action);
        Assert.True(ex.TryGetMessageKey(out string key, out _));
        Assert.Equal(messageKey, key);
    }
}

internal sealed class TempDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "iscWBS.Tests", Guid.NewGuid().ToString("N"));

    public TempDirectory() => Directory.CreateDirectory(Path);

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose() => Directory.Delete(Path, recursive: true);
}
