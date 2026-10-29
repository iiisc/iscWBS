using System.Security.Cryptography;
using System.Text;
using iscWBS.Core.Services;

namespace iscWBS.Tests;

public sealed class UpdateServiceTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly string _release;
    private readonly string _app;
    private readonly string _exe;

    public UpdateServiceTests()
    {
        _release = Directory.CreateDirectory(_dir.File("release")).FullName;
        _app = Directory.CreateDirectory(_dir.File("app")).FullName;
        _exe = Path.Combine(_app, "iscWBS.exe");
        File.WriteAllText(_exe, "old exe");
        File.WriteAllText(Path.Combine(_app, "my project.iscwbs"), "user data");
        File.WriteAllText(Path.Combine(_release, "iscWBS.exe"), "new exe");
        Directory.CreateDirectory(Path.Combine(_release, "sv-SE"));
        File.WriteAllText(Path.Combine(_release, "sv-SE", "strings.mui"), "new strings");
    }

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void SwapIn_ReplacesAndAddsFiles_MovesOldOnesAside_AndLeavesOtherFilesAlone()
    {
        UpdateService.SwapIn(_release, _exe);

        Assert.Equal("new exe", File.ReadAllText(_exe));
        Assert.Equal("new strings", File.ReadAllText(Path.Combine(_app, "sv-SE", "strings.mui")));
        Assert.Equal("old exe", File.ReadAllText(Path.Combine(_app, UpdateService.OldVersionDir, "iscWBS.exe")));
        Assert.Equal("user data", File.ReadAllText(Path.Combine(_app, "my project.iscwbs")));
    }

    [Fact]
    public void SwapIn_PutsEverythingBack_WhenAFileCannotBeReplaced()
    {
        File.WriteAllText(Path.Combine(_release, "zz-locked.dll"), "new");
        string locked = Path.Combine(_app, "zz-locked.dll");
        File.WriteAllText(locked, "old");

        using (File.Open(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.ThrowsAny<IOException>(() => UpdateService.SwapIn(_release, _exe));

        Assert.Equal("old exe", File.ReadAllText(_exe));
        Assert.False(File.Exists(Path.Combine(_app, "sv-SE", "strings.mui")));
        Assert.Equal("old", File.ReadAllText(locked));
    }

    [Fact]
    public void SwapIn_RefusesAPackageWithoutTheExe()
    {
        File.Delete(Path.Combine(_release, "iscWBS.exe"));

        Assert.Throws<InvalidDataException>(() => UpdateService.SwapIn(_release, _exe));
        Assert.Equal("old exe", File.ReadAllText(_exe));
    }

    [Fact]
    public void Sha256Matches_ComparesTheFileHash_IgnoringCase()
    {
        string path = _dir.File("update.zip");
        File.WriteAllText(path, "release");
        string hex = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("release")));

        Assert.True(UpdateService.Sha256Matches(path, hex.ToLowerInvariant()));
        Assert.False(UpdateService.Sha256Matches(path, new string('0', 64)));
    }
}
