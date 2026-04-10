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

    [ObservableProperty]
    private string _currentVersion = string.Empty;

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

    public SettingsViewModel(
        ISettingsService settingsService,
        IUpdateService updateService,
        IDialogService dialogService)
    {
        _settingsService = settingsService;
        _updateService = updateService;
        _dialogService = dialogService;
        _currentVersion = _updateService.CurrentVersion;
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
}
