using CommunityToolkit.Mvvm.ComponentModel;
using iscWBS.Core.Services;

namespace iscWBS.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;

    public SettingsViewModel(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }
}
