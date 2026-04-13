using System.Text.Json;
using iscWBS.Helpers;

namespace iscWBS.Core.Services;

public sealed class SettingsService : ISettingsService
{
    private static readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ISC", "iscWBS", "settings.json");

    private readonly Dictionary<string, JsonElement> _values;

    public SettingsService()
    {
        _values = Load();
    }

    public T? Get<T>(string key)
    {
        if (_values.TryGetValue(key, out JsonElement element))
        {
            try { return element.Deserialize<T>(); }
            catch { return default; }
        }
        return default;
    }

    public void Set<T>(string key, T value)
    {
        _values[key] = JsonSerializer.SerializeToElement(value);
        Save();
    }

    public IReadOnlyList<string> GetRecentProjects()
        => Get<List<string>>(SettingsKeys.RecentProjects) ?? [];

    public void AddRecentProject(string filePath)
    {
        List<string> projects = GetRecentProjects().ToList();
        projects.Remove(filePath);
        projects.Insert(0, filePath);
        if (projects.Count > 10)
            projects = projects[..10];
        Set(SettingsKeys.RecentProjects, projects);
    }

    public void RemoveRecentProject(string filePath)
    {
        List<string> projects = GetRecentProjects().ToList();
        if (projects.Remove(filePath))
            Set(SettingsKeys.RecentProjects, projects);
    }

    private static Dictionary<string, JsonElement> Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                string json = File.ReadAllText(_settingsPath);
                return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? new();
            }
        }
        catch { }
        return new();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            File.WriteAllText(_settingsPath,
                JsonSerializer.Serialize(_values, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}

