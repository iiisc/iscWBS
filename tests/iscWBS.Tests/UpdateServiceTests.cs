using System.Security.Cryptography;
using System.Text;
using iscWBS.Core.Services;

namespace iscWBS.Tests;

public sealed class UpdateServiceTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void CopyOver_ReplacesAndAddsFiles_AndLeavesOtherFilesAlone()
    {
        string source = Directory.CreateDirectory(_dir.File("staged")).FullName;
        string target = Directory.CreateDirectory(_dir.File("installed")).FullName;
        File.WriteAllText(Path.Combine(source, "iscWBS.exe"), "new");
        Directory.CreateDirectory(Path.Combine(source, "sv-SE"));
        File.WriteAllText(Path.Combine(source, "sv-SE", "res.pri"), "new resources");
        File.WriteAllText(Path.Combine(target, "iscWBS.exe"), "old");
        File.WriteAllText(Path.Combine(target, "my project.iscwbs"), "user data");

        UpdateService.CopyOver(source, target);

        Assert.Equal("new", File.ReadAllText(Path.Combine(target, "iscWBS.exe")));
        Assert.Equal("new resources", File.ReadAllText(Path.Combine(target, "sv-SE", "res.pri")));
        Assert.Equal("user data", File.ReadAllText(Path.Combine(target, "my project.iscwbs")));
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
