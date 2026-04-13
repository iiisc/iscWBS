namespace iscWBS.Core.Services;

/// <summary>Provides persistent user preferences backed by a JSON file in <c>%LOCALAPPDATA%\ISC\iscWBS\</c>.</summary>
public interface ISettingsService
{
    /// <summary>Gets a setting value by key, or <see langword="default"/> if not found.</summary>
    T? Get<T>(string key);

    /// <summary>Persists a setting value by key.</summary>
    void Set<T>(string key, T value);

    /// <summary>Returns the list of recently opened project file paths, most recent first.</summary>
    IReadOnlyList<string> GetRecentProjects();

    /// <summary>Adds a file path to the front of the recent projects list (max 10 entries).</summary>
    void AddRecentProject(string filePath);

    /// <summary>Removes a file path from the recent projects list.</summary>
    void RemoveRecentProject(string filePath);
}
