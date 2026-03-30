namespace iscWBS.Core.Services;

/// <summary>Wraps <c>ApplicationData.LocalSettings</c> for persistent user preferences.</summary>
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
}
