using System.Collections.ObjectModel;
using System.Net;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iscWBS.Core.Models;
using iscWBS.Core.Services;
using iscWBS.Helpers;

namespace iscWBS.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly UpdateService _updateService;
    private readonly DialogService _dialogService;
    private readonly ProjectStateService _projectStateService;
    private readonly NavigationService _navigationService;
    private readonly LocalizationService _localizationService;

    public string CurrentVersion { get; }

    public ObservableCollection<string> RecentProjects { get; } = new();

    public bool HasActiveProject => _projectStateService.HasActiveProject;
    public bool HasNoActiveProject => !HasActiveProject;
    public bool HasRecentProjects => RecentProjects.Count > 0;

    public string ActiveProjectName =>
        _projectStateService.ActiveProject?.Name ?? string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    public partial string UpdateStatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrorMessage))]
    public partial string UpdateErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsCheckingForUpdate { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotDownloadingUpdate))]
    public partial bool IsDownloadingUpdate { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUpdateAvailable))]
    public partial UpdateInfo? AvailableUpdate { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DownloadProgressText))]
    public partial int DownloadProgress { get; set; }

    public bool IsUpdateAvailable => AvailableUpdate is not null;
    public bool HasStatusMessage => !string.IsNullOrEmpty(UpdateStatusMessage);
    public bool HasErrorMessage => !string.IsNullOrEmpty(UpdateErrorMessage);
    public bool IsNotDownloadingUpdate => !IsDownloadingUpdate;
    public string DownloadProgressText => $"{DownloadProgress}%";

    public List<LanguageOption> AvailableLanguages { get; } = [
        new("en-US", "English"),
        new("sv-SE", "Svenska")
    ];

    [ObservableProperty]
    public partial LanguageOption? SelectedLanguage { get; set; }

    [ObservableProperty]
    public partial bool RestartRequired { get; set; }

    public SettingsViewModel(
        SettingsService settingsService,
        UpdateService updateService,
        DialogService dialogService,
        ProjectStateService projectStateService,
        NavigationService navigationService,
        LocalizationService localizationService)
    {
        _settingsService = settingsService;
        _updateService = updateService;
        _dialogService = dialogService;
        _projectStateService = projectStateService;
        _navigationService = navigationService;
        _localizationService = localizationService;
        CurrentVersion = _updateService.CurrentVersion;
        SelectedLanguage = AvailableLanguages.FirstOrDefault(l => l.Code == localizationService.CurrentLanguage)
            ?? AvailableLanguages[0];

        RecentProjects.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasRecentProjects));
        LoadRecentProjects();
    }

    // The project-state service is a singleton; unsubscribing on leave keeps it from holding every visited page alive.
    public void OnNavigatedTo() => _projectStateService.ActiveProjectChanged += OnActiveProjectChanged;
    public void OnNavigatedFrom() => _projectStateService.ActiveProjectChanged -= OnActiveProjectChanged;

    private void OnActiveProjectChanged(object? sender, Project? project)
    {
        OnPropertyChanged(nameof(HasActiveProject));
        OnPropertyChanged(nameof(HasNoActiveProject));
        OnPropertyChanged(nameof(ActiveProjectName));
        LoadRecentProjects();
    }

    private void LoadRecentProjects()
    {
        RecentProjects.Clear();
        foreach (string path in _settingsService.GetRecentProjects())
            RecentProjects.Add(path);
    }

    public async Task CreateProjectAsync(string name, string filePath, string owner)
    {
        try
        {
            await _projectStateService.CreateProjectAsync(name, filePath, owner);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("ErrorTitle_CreateProject", ex);
        }
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

    [RelayCommand]
    private async Task CloseProjectAsync()
    {
        try
        {
            await _projectStateService.CloseProjectAsync();
            _navigationService.NavigateTo("WelcomePage");
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("ErrorTitle_CloseProject", ex);
        }
    }

    [RelayCommand]
    private void RemoveRecentProject(string filePath)
    {
        _settingsService.RemoveRecentProject(filePath);
        RecentProjects.Remove(filePath);
    }

    [RelayCommand]
    private async Task CheckForUpdateAsync()
    {
        IsCheckingForUpdate = true;
        UpdateStatusMessage = string.Empty;
        UpdateErrorMessage = string.Empty;
        AvailableUpdate = null;
        try
        {
            UpdateInfo? update = await _updateService.CheckForUpdateAsync();
            if (update is null)
            {
                UpdateStatusMessage = Strings.Get("Settings_UpToDate");
            }
            else
            {
                AvailableUpdate = update;
            }
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            UpdateErrorMessage = Strings.Get("Settings_RepositoryNotFound");
            Logger.Write(ex);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            UpdateErrorMessage = Strings.Get("Settings_RateLimited");
            Logger.Write(ex);
        }
        catch (Exception ex)
        {
            UpdateErrorMessage = Strings.Get("Settings_CheckFailed");
            Logger.Write(ex);
        }
        finally
        {
            IsCheckingForUpdate = false;
        }
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task DownloadAndInstallAsync(CancellationToken cancellationToken)
    {
        if (AvailableUpdate is null) return;

        IsDownloadingUpdate = true;
        DownloadProgress = 0;
        try
        {
            Progress<int> progress = new(p => DownloadProgress = p);
            await _updateService.DownloadAndInstallAsync(AvailableUpdate, progress, cancellationToken);

            // The updater script is waiting for this process to exit.
            if (_projectStateService.HasActiveProject)
                await _projectStateService.CloseProjectAsync();

            Environment.Exit(0);
        }
        catch (OperationCanceledException)
        {
            UpdateStatusMessage = Strings.Get("Settings_DownloadCancelled");
        }
        catch (Exception ex)
        {
            UpdateErrorMessage = Strings.Get("Settings_DownloadFailed");
            Logger.Write(ex);
        }
        finally
        {
            IsDownloadingUpdate = false;
        }
    }

    [RelayCommand]
    private async Task OpenReleasePageAsync()
    {
        if (AvailableUpdate is null) return;
        try
        {
            _updateService.OpenReleasePage(AvailableUpdate);
        }
        catch (Exception ex)
        {
            Logger.Write(ex);
            await _dialogService.ShowErrorAsync(Strings.Get("ErrorTitle_Generic"), Strings.Get("Settings_OpenReleasePageFailed"));
        }
    }

    [RelayCommand]
    private void ApplyLanguage()
    {
        if (SelectedLanguage is null) return;
        _localizationService.SetLanguage(SelectedLanguage.Code);
        RestartRequired = true;
    }
}
