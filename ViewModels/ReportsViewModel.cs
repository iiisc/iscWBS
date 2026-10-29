using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Windows.Storage;
using Windows.System;
using iscWBS.Core.Models;
using iscWBS.Core.Services;
using iscWBS.Helpers;

namespace iscWBS.ViewModels;

public sealed partial class ReportsViewModel : ObservableObject
{
    private readonly ReportExportService _reportExportService;
    private readonly DialogService _dialogService;
    private readonly SettingsService _settingsService;

    [ObservableProperty]
    public partial string? LastExportPath { get; set; }

    public bool HasLastExportPath => !string.IsNullOrEmpty(LastExportPath);

    partial void OnLastExportPathChanged(string? value) => OnPropertyChanged(nameof(HasLastExportPath));

    [ObservableProperty]
    public partial bool IncludeSummary { get; set; }

    [ObservableProperty]
    public partial bool IncludeAttentionRequired { get; set; }

    [ObservableProperty]
    public partial bool IncludeDeliverables { get; set; }

    [ObservableProperty]
    public partial bool IncludeEffortSummary { get; set; }

    [ObservableProperty]
    public partial bool IncludeMilestones { get; set; }

    [ObservableProperty]
    public partial bool IncludeWbsTree { get; set; }

    partial void OnIncludeSummaryChanged(bool value)           => _settingsService.Set(SettingsKeys.ReportIncludeSummary,           value);
    partial void OnIncludeAttentionRequiredChanged(bool value) => _settingsService.Set(SettingsKeys.ReportIncludeAttentionRequired, value);
    partial void OnIncludeDeliverablesChanged(bool value)      => _settingsService.Set(SettingsKeys.ReportIncludeDeliverables,      value);
    partial void OnIncludeEffortSummaryChanged(bool value)     => _settingsService.Set(SettingsKeys.ReportIncludeEffortSummary,     value);
    partial void OnIncludeMilestonesChanged(bool value)        => _settingsService.Set(SettingsKeys.ReportIncludeMilestones,        value);
    partial void OnIncludeWbsTreeChanged(bool value)           => _settingsService.Set(SettingsKeys.ReportIncludeWbsTree,           value);

    public ReportsViewModel(
        ReportExportService reportExportService,
        DialogService dialogService,
        SettingsService settingsService)
    {
        _reportExportService = reportExportService;
        _dialogService       = dialogService;
        _settingsService     = settingsService;

        IncludeSummary           = _settingsService.Get<bool?>(SettingsKeys.ReportIncludeSummary)           ?? true;
        IncludeAttentionRequired = _settingsService.Get<bool?>(SettingsKeys.ReportIncludeAttentionRequired) ?? true;
        IncludeDeliverables      = _settingsService.Get<bool?>(SettingsKeys.ReportIncludeDeliverables)      ?? true;
        IncludeEffortSummary     = _settingsService.Get<bool?>(SettingsKeys.ReportIncludeEffortSummary)     ?? true;
        IncludeMilestones        = _settingsService.Get<bool?>(SettingsKeys.ReportIncludeMilestones)        ?? true;
        IncludeWbsTree           = _settingsService.Get<bool?>(SettingsKeys.ReportIncludeWbsTree)           ?? true;
    }

    [RelayCommand]
    private async Task ExportPdfAsync()
    {
        string? path = await FilePickers.PickStatusReportSaveFileAsync();
        if (path is null) return;
        try
        {
            var options = new ReportOptions
            {
                IncludeSummary           = IncludeSummary,
                IncludeAttentionRequired = IncludeAttentionRequired,
                IncludeDeliverables      = IncludeDeliverables,
                IncludeEffortSummary     = IncludeEffortSummary,
                IncludeMilestones        = IncludeMilestones,
                IncludeWbsTree           = IncludeWbsTree,
            };
            await _reportExportService.ExportAsync(path, options);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("ErrorTitle_Export", ex);
            return;
        }
        LastExportPath = path;
        await Launcher.LaunchFileAsync(await StorageFile.GetFileFromPathAsync(path));
    }
}
