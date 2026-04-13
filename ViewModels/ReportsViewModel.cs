using CommunityToolkit.Mvvm.ComponentModel;
using iscWBS.Core.Models;
using iscWBS.Core.Services;
using iscWBS.Helpers;

namespace iscWBS.ViewModels;

public sealed partial class ReportsViewModel : ObservableObject, INavigationAware
{
    private readonly IReportExportService _reportExportService;
    private readonly IDialogService _dialogService;
    private readonly ISettingsService _settingsService;

    // â”€â”€â”€ Export state â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [ObservableProperty]
    public partial bool IsExporting { get; set; }

    [ObservableProperty]
    public partial string? LastExportPath { get; set; }

    public bool HasLastExportPath => !string.IsNullOrEmpty(LastExportPath);

    partial void OnLastExportPathChanged(string? value) => OnPropertyChanged(nameof(HasLastExportPath));

    // â”€â”€â”€ Section options â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

    partial void OnIncludeSummaryChanged(bool v)           => _settingsService.Set(SettingsKeys.ReportIncludeSummary,           v);
    partial void OnIncludeAttentionRequiredChanged(bool v) => _settingsService.Set(SettingsKeys.ReportIncludeAttentionRequired, v);
    partial void OnIncludeDeliverablesChanged(bool v)      => _settingsService.Set(SettingsKeys.ReportIncludeDeliverables,      v);
    partial void OnIncludeEffortSummaryChanged(bool v)     => _settingsService.Set(SettingsKeys.ReportIncludeEffortSummary,     v);
    partial void OnIncludeMilestonesChanged(bool v)        => _settingsService.Set(SettingsKeys.ReportIncludeMilestones,        v);
    partial void OnIncludeWbsTreeChanged(bool v)           => _settingsService.Set(SettingsKeys.ReportIncludeWbsTree,           v);

    public ReportsViewModel(
        IReportExportService reportExportService,
        IDialogService dialogService,
        ISettingsService settingsService)
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

    public void OnNavigatedTo(object? parameter) { }
    public void OnNavigatedFrom() { }

    /// <summary>
    /// Generates a PDF report with the user-selected sections and saves it to <paramref name="filePath"/>.
    /// Returns <see langword="true"/> on success; errors are surfaced via <see cref="IDialogService"/>.
    /// </summary>
    public async Task<bool> ExportPdfAsync(string filePath)
    {
        if (IsExporting) return false;
        IsExporting = true;
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
            await _reportExportService.ExportAsync(filePath, options);
            LastExportPath = filePath;
            return true;
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Export Failed", ex.Message);
            return false;
        }
        finally
        {
            IsExporting = false;
        }
    }
}

