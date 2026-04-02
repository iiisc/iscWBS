using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iscWBS.Core.Services;

namespace iscWBS.ViewModels;

public sealed partial class WelcomeViewModel : ObservableObject
{
    private readonly IProjectStateService _projectStateService;
    private readonly ISettingsService _settingsService;
    private readonly IDialogService _dialogService;

    [ObservableProperty]
    public partial ObservableCollection<string> RecentProjects { get; set; } = new();

    public bool HasRecentProjects => RecentProjects.Count > 0;

    public WelcomeViewModel(
        IProjectStateService projectStateService,
        ISettingsService settingsService,
        IDialogService dialogService)
    {
        _projectStateService = projectStateService;
        _settingsService = settingsService;
        _dialogService = dialogService;

        RecentProjects.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasRecentProjects));
        LoadRecentProjects();
    }

    private void LoadRecentProjects()
    {
        RecentProjects.Clear();
        foreach (string path in _settingsService.GetRecentProjects())
            RecentProjects.Add(path);
    }

    /// <summary>Called from WelcomePage code-behind after a successful file picker result.</summary>
    public async Task OpenProjectAsync(string filePath)
    {
        try
        {
            await _projectStateService.OpenProjectAsync(filePath);
            _settingsService.AddRecentProject(filePath);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Cannot Open Project", ex.Message);
        }
    }

    /// <summary>Called from WelcomePage code-behind after the New Project dialog is confirmed.</summary>
    public async Task CreateProjectAsync(string name, string owner, string currency, string folderPath)
    {
        try
        {
            string filePath = Path.Combine(folderPath, $"{name}.iscwbs");
            await _projectStateService.CreateProjectAsync(name, filePath, owner, currency);
            _settingsService.AddRecentProject(filePath);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Cannot Create Project", ex.Message);
        }
    }

    [RelayCommand]
    private async Task OpenRecentProjectAsync(string filePath)
        => await OpenProjectAsync(filePath);
}
