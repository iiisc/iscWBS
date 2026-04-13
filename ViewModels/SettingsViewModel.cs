using System.Collections.ObjectModel;
using System.IO;
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
    private readonly ISettingsService _settingsService;
    private readonly IUpdateService _updateService;
    private readonly IDialogService _dialogService;
    private readonly IProjectStateService _projectStateService;
    private readonly INavigationService _navigationService;
    private readonly ILocalizationService _localizationService;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveProjectName))]
    [NotifyPropertyChangedFor(nameof(HasNoActiveProject))]
    private bool _hasActiveProject;

    [ObservableProperty]
    private string _currentVersion = string.Empty;

    [ObservableProperty]
    public partial ObservableCollection<string> RecentProjects { get; set; } = new();

    public bool HasNoActiveProject => !HasActiveProject;
    public bool HasRecentProjects => RecentProjects.Count > 0;

    public string ActiveProjectName =>
        _projectStateService.ActiveProject?.Name ?? string.Empty;

    public string DefaultProjectFolder =>
        _settingsService.Get<string>(SettingsKeys.LastProjectFolder)
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "iscWBS Projects");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string _updateStatusMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrorMessage))]
    private string _updateErrorMessage = string.Empty;

    [ObservableProperty]
    private bool _isCheckingForUpdate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotDownloadingUpdate))]
    private bool _isDownloadingUpdate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUpdateAvailable))]
    private UpdateInfo? _availableUpdate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DownloadProgressText))]
    private int _downloadProgress;

    public bool IsUpdateAvailable => AvailableUpdate is not null;
    public bool HasStatusMessage => !string.IsNullOrEmpty(UpdateStatusMessage);
    public bool HasErrorMessage => !string.IsNullOrEmpty(UpdateErrorMessage);
    public bool IsNotDownloadingUpdate => !IsDownloadingUpdate;
    public string DownloadProgressText => $"{DownloadProgress}%";

    public IReadOnlyList<LanguageOption> AvailableLanguages { get; } = [
        new("en-US", "English"),
        new("sv-SE", "Svenska")
    ];

    [ObservableProperty]
    private LanguageOption? _selectedLanguage;

    [ObservableProperty]
    private bool _restartRequired;

    public SettingsViewModel(
        ISettingsService settingsService,
        IUpdateService updateService,
        IDialogService dialogService,
        IProjectStateService projectStateService,
        INavigationService navigationService,
        ILocalizationService localizationService)
    {
        _settingsService = settingsService;
        _updateService = updateService;
        _dialogService = dialogService;
        _projectStateService = projectStateService;
        _navigationService = navigationService;
        _localizationService = localizationService;
        _currentVersion = _updateService.CurrentVersion;
        _hasActiveProject = _projectStateService.HasActiveProject;
        _selectedLanguage = AvailableLanguages.FirstOrDefault(l => l.Code == localizationService.CurrentLanguage)
            ?? AvailableLanguages[0];

        _projectStateService.ActiveProjectChanged += OnActiveProjectChanged;
        RecentProjects.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasRecentProjects));
        LoadRecentProjects();
    }

    private void OnActiveProjectChanged(object? sender, Project? project)
    {
        HasActiveProject = project is not null;
        OnPropertyChanged(nameof(ActiveProjectName));
        LoadRecentProjects();
    }

    private void LoadRecentProjects()
    {
        RecentProjects.Clear();
        foreach (string path in _settingsService.GetRecentProjects())
            RecentProjects.Add(path);
    }

    /// <summary>Called from SettingsPage code-behind after the New Project dialog is confirmed.</summary>
    public async Task CreateProjectAsync(string name, string owner, string folderPath)
    {
        try
        {
            string filePath = Path.Combine(folderPath, $"{name}.iscwbs");
            await _projectStateService.CreateProjectAsync(name, filePath, owner);
            _settingsService.AddRecentProject(filePath);
            _settingsService.Set(SettingsKeys.LastProjectFolder, folderPath);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Cannot Create Project", ex.Message);
        }
    }

    /// <summary>Called from SettingsPage code-behind after a successful file picker result.</summary>
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
            await _dialogService.ShowErrorAsync("Cannot Close Project", ex.Message);
            Logger.Write(ex);
        }
    }

    [RelayCommand]
    private void RemoveRecentProject(string filePath)
    {
        _settingsService.RemoveRecentProject(filePath);
        RecentProjects.Remove(filePath);
    }

    [RelayCommand]
    private async Task OpenRecentProjectAsync(string filePath)
        => await OpenProjectAsync(filePath);

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
                UpdateStatusMessage = "You are running the latest version.";
            }
            else
            {
                AvailableUpdate = update;
            }
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            UpdateErrorMessage = "The update repository could not be found. The project may have moved or been renamed.";
            Logger.Write(ex);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            UpdateErrorMessage = "GitHub API rate limit reached. Please wait a few minutes and try again.";
            Logger.Write(ex);
        }
        catch (Exception ex)
        {
            UpdateErrorMessage = "Could not check for updates. Please check your internet connection.";
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
        }
        catch (OperationCanceledException)
        {
            UpdateStatusMessage = "Download cancelled.";
        }
        catch (Exception ex)
        {
            UpdateErrorMessage = "Download failed. Please try again or visit the release page.";
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
            await _updateService.OpenReleasePageAsync(AvailableUpdate);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Error", "Could not open the release page.");
            Logger.Write(ex);
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
