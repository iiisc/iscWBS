using iscWBS.Core.Services;

namespace iscWBS.Tests;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    // The release build has no reflection-based JSON, so every type the app stores must be in AppJsonContext.
    [Fact]
    public void EveryStoredType_RoundTripsThroughTheFile()
    {
        string path = _dir.File("settings.json");
        var settings = new SettingsService(path);
        settings.Set("text", "sv-SE");
        settings.Set("flag", false);
        settings.AddRecentProject(@"C:\a.iscwbs");

        var reloaded = new SettingsService(path);

        Assert.Equal("sv-SE", reloaded.Get<string>("text"));
        Assert.False(reloaded.Get<bool?>("flag"));
        Assert.Equal([@"C:\a.iscwbs"], reloaded.GetRecentProjects());
    }

    [Fact]
    public void UnregisteredType_FailsLoudly()
        => Assert.Throws<NotSupportedException>(() => new SettingsService(_dir.File("settings.json")).Get<DateTime>("x"));
}
