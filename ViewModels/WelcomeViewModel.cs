using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using iscWBS.Core.Services;
using iscWBS.Helpers;

namespace iscWBS.ViewModels;

public sealed partial class WelcomeViewModel : ObservableObject
{
    private readonly ProjectStateService _projectStateService;
    private readonly SettingsService _settingsService;
    private readonly DialogService _dialogService;

    public ObservableCollection<string> RecentProjects { get; } = new();

    public bool HasRecentProjects => RecentProjects.Count > 0;

    public WelcomeViewModel(
        ProjectStateService projectStateService,
        SettingsService settingsService,
        DialogService dialogService)
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

    public async Task OpenProjectAsync(string filePath)
    {
        try
        {
            await _projectStateService.OpenProjectAsync(filePath);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("ErrorTitle_OpenProject", ex);
        }
    }

    /// <param name="sourcePath">A project whose plan to copy, or a workbook to import; null for an empty project.</param>
    public async Task CreateProjectAsync(string name, string filePath, string owner, string? sourcePath)
    {
        try
        {
            ProjectContent? content = sourcePath is null ? null : await ProjectContent.FromFileAsync(sourcePath, Strings.Get);
            await _projectStateService.CreateProjectAsync(name, filePath, owner, content);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("ErrorTitle_CreateProject", ex);
        }
    }
}
