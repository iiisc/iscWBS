using System.Globalization;
using Microsoft.Windows.Globalization;
using iscWBS.Helpers;

namespace iscWBS.Core.Services;

/// <summary>Persists the UI language and applies it through <see cref="ApplicationLanguages"/>; strings come from <see cref="Strings"/>.</summary>
public sealed class LocalizationService
{
    private readonly SettingsService _settingsService;

    public LocalizationService(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public string CurrentLanguage =>
        _settingsService.Get<string>(SettingsKeys.Language)
        ?? (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "sv" ? "sv-SE" : "en-US");

    public void SetLanguage(string languageCode)
    {
        _settingsService.Set(SettingsKeys.Language, languageCode);
        ApplicationLanguages.PrimaryLanguageOverride = languageCode;
    }
}
