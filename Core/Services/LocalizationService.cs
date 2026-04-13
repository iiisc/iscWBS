using System.Runtime.InteropServices;
using Windows.ApplicationModel.Resources;
using Windows.Globalization;
using iscWBS.Helpers;

namespace iscWBS.Core.Services;

/// <summary>
/// Wraps the Windows PRI resource loader and <see cref="ApplicationLanguages"/>
/// to provide localised strings and language switching.
/// </summary>
public sealed class LocalizationService : ILocalizationService
{
    private readonly ISettingsService _settingsService;
    private ResourceLoader? _resourceLoader;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int length, char[]? name);

    public LocalizationService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    /// <inheritdoc />
    public string CurrentLanguage =>
        _settingsService.Get<string>(SettingsKeys.Language) ?? "en-US";

    /// <inheritdoc />
    public string GetString(string key)
    {
        try
        {
            _resourceLoader ??= ResourceLoader.GetForViewIndependentUse();
            string value = _resourceLoader.GetString(key);
            return string.IsNullOrEmpty(value) ? key : value;
        }
        catch
        {
            return key;
        }
    }

    /// <inheritdoc />
    public void SetLanguage(string languageCode)
    {
        _settingsService.Set(SettingsKeys.Language, languageCode);
        if (IsPackaged())
        {
            ApplicationLanguages.PrimaryLanguageOverride = languageCode;
        }
    }

    /// <summary>Returns <see langword="true"/> when the process has MSIX package identity.</summary>
    private static bool IsPackaged()
    {
        int length = 0;
        const int AppmodelErrorNoPackage = 15700;
        return GetCurrentPackageFullName(ref length, null) != AppmodelErrorNoPackage;
    }
}
