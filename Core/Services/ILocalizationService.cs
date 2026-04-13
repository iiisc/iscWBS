namespace iscWBS.Core.Services;

/// <summary>
/// Provides language selection and localised string lookup for the application.
/// </summary>
public interface ILocalizationService
{
    /// <summary>
    /// Gets the currently active language code (e.g. <c>"en-US"</c> or <c>"sv-SE"</c>).
    /// </summary>
    string CurrentLanguage { get; }

    /// <summary>
    /// Returns the localised string for the given resource key from <c>Resources.resw</c>.
    /// Returns <paramref name="key"/> itself when no matching resource is found so callers
    /// never receive a null or empty value.
    /// </summary>
    string GetString(string key);

    /// <summary>
    /// Persists <paramref name="languageCode"/> to settings and sets
    /// <see cref="Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride"/>
    /// for the current session. The new language is fully applied after the application
    /// is restarted.
    /// </summary>
    void SetLanguage(string languageCode);
}
