using System.Text.Json;
using Windows.Storage;
using iscWBS.Helpers;

namespace iscWBS.Core.Services;

public sealed class SettingsService : ISettingsService
{
    private readonly ApplicationDataContainer _settings = ApplicationData.Current.LocalSettings;

    public T? Get<T>(string key)
    {
        if (_settings.Values.TryGetValue(key, out object? raw) && raw is T value)
            return value;
        return default;
    }

    public void Set<T>(string key, T value) => _settings.Values[key] = value;

    public IReadOnlyList<string> GetRecentProjects()
    {
        string? json = Get<string>(SettingsKeys.RecentProjects);
        if (json is null) return Array.Empty<string>();
        return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
    }

    public void AddRecentProject(string filePath)
    {
        List<string> projects = GetRecentProjects().ToList();
        projects.Remove(filePath);
        projects.Insert(0, filePath);
        if (projects.Count > 10)
            projects = projects[..10];
        Set(SettingsKeys.RecentProjects, JsonSerializer.Serialize(projects));
    }
}
