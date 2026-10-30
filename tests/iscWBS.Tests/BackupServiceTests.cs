using iscWBS.Core.Services;

namespace iscWBS.Tests;

public sealed class BackupServiceTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    private string[] BackupsOf(BackupService backups, string project) =>
        Directory.GetFiles(backups.FolderFor(project), "*.iscwbs");

    [Fact]
    public async Task OpeningAndClosingAProject_KeepsACopy_ButNotOneWithoutChanges()
    {
        var backups = new BackupService(_dir.File("backups"));
        var state = new ProjectStateService(new SettingsService(_dir.File("settings.json")), backups);
        string path = _dir.File("plan.iscwbs");

        await state.CreateProjectAsync("Plan", path);
        await state.CloseProjectAsync();
        Assert.Single(BackupsOf(backups, path));

        await state.OpenProjectAsync(path);
        await new WbsService(state).AddRootNodeAsync(state.ActiveProject!.Id, "Root");
        await Task.Delay(1100); // Copies are named by the second.
        await state.CloseProjectAsync();

        string[] copies = BackupsOf(backups, path);
        Assert.Equal(2, copies.Length);
        Assert.Contains(copies, c => new FileInfo(c).Length == new FileInfo(path).Length
                                     && File.ReadAllBytes(c).AsSpan().SequenceEqual(File.ReadAllBytes(path)));
    }

    [Fact]
    public void KeepsOnlyTheNewestCopies_AndSeparatesSameNamedProjects()
    {
        var backups = new BackupService(_dir.File("backups"));
        string first = _dir.File("a/plan.iscwbs");
        string second = _dir.File("b/plan.iscwbs");
        Directory.CreateDirectory(Path.GetDirectoryName(first)!);
        Directory.CreateDirectory(Path.GetDirectoryName(second)!);
        File.WriteAllText(second, "other");

        string folder = backups.FolderFor(first);
        Directory.CreateDirectory(folder);
        for (int i = 0; i < BackupService.Keep + 3; i++)
            File.WriteAllText(Path.Combine(folder, $"2020-01-01_0000{i:00}.iscwbs"), $"old {i}");
        File.WriteAllText(first, "current");

        backups.BackUp(first);
        backups.BackUp(second);

        string[] kept = BackupsOf(backups, first).Order().ToArray();
        Assert.Equal(BackupService.Keep, kept.Length);
        Assert.Equal("current", File.ReadAllText(kept[^1]));
        Assert.DoesNotContain(kept, k => k.EndsWith("2020-01-01_000000.iscwbs"));
        Assert.NotEqual(backups.FolderFor(first), backups.FolderFor(second));
        Assert.Single(BackupsOf(backups, second));
    }

    [Fact]
    public void AFailedCopy_IsReported_NotThrown()
    {
        Exception? reported = null;
        var backups = new BackupService(_dir.File("backups"), ex => reported = ex);

        backups.BackUp(_dir.File("missing.iscwbs"));

        Assert.IsType<FileNotFoundException>(reported);
    }
}
