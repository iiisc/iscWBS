using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using iscWBS.Core.Data;
using iscWBS.Helpers;

namespace iscWBS.Core.Services;

/// <summary>User preferences, stored as JSON in <c>%LOCALAPPDATA%\ISC\iscWBS\settings.json</c>.</summary>
public sealed class SettingsService
{
    /// <summary><c>%LOCALAPPDATA%\ISC\iscWBS</c>; the log folder lives here too.</summary>
    public static readonly string AppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ISC", "iscWBS");

    private readonly string _settingsPath;
    private readonly Dictionary<string, JsonElement> _values;

    public SettingsService(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(AppDataFolder, "settings.json");
        _values = Load();
    }

    public T? Get<T>(string key)
    {
        JsonTypeInfo<T> typeInfo = TypeInfo<T>();
        if (_values.TryGetValue(key, out JsonElement element))
        {
            try { return element.Deserialize(typeInfo); }
            catch { return default; }
        }
        return default;
    }

    public void Set<T>(string key, T value)
    {
        JsonElement element = JsonSerializer.SerializeToElement(value, TypeInfo<T>());
        if (_values.TryGetValue(key, out JsonElement existing) && existing.GetRawText() == element.GetRawText())
            return;
        _values[key] = element;
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

    public string GetDefaultProjectFolder()
        => Get<string>(SettingsKeys.LastProjectFolder)
           ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "iscWBS Projects");

    private static JsonTypeInfo<T> TypeInfo<T>()
        => (JsonTypeInfo<T>?)AppJsonContext.Default.GetTypeInfo(typeof(T))
           ?? throw new NotSupportedException($"{typeof(T)} must be added to {nameof(AppJsonContext)}.");

    private Dictionary<string, JsonElement> Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                string json = File.ReadAllText(_settingsPath);
                return JsonSerializer.Deserialize(json, AppJsonContext.Default.DictionaryStringJsonElement) ?? new();
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
                JsonSerializer.Serialize(_values, AppJsonContext.Default.DictionaryStringJsonElement));
        }
        catch { }
    }
}

