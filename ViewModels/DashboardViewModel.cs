using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using iscWBS.Core.Models;
using iscWBS.Core.Services;
using iscWBS.Helpers;
using Windows.Storage;
using Windows.System;

namespace iscWBS.ViewModels;

public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly ProjectStateService _projectStateService;
    private readonly WbsService _wbsService;
    private readonly MilestoneService _milestoneService;
    private readonly DialogService _dialogService;
    private readonly ReportExportService _reportExportService;

    private IReadOnlyList<WbsNode> _allNodes = Array.Empty<WbsNode>();
    private IReadOnlySet<Guid> _blockedIds = new HashSet<Guid>();
    private IReadOnlyList<Milestone> _allMilestones = Array.Empty<Milestone>();

    [ObservableProperty]
    public partial DateTimeOffset? FilterStartDate { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset? FilterEndDate { get; set; }

    [ObservableProperty]
    public partial string? SelectedAssignee { get; set; }

    [ObservableProperty]
    public partial WbsStatus? SelectedStatus { get; set; }

    public ObservableCollection<string> AssigneeOptions { get; } = new();

    private readonly string _allAssignees;

    public IReadOnlyList<WbsStatus?> StatusOptions { get; } =
        new WbsStatus?[] { null }
            .Concat(Enum.GetValues<WbsStatus>().Cast<WbsStatus?>())
            .ToList();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OverallPercentText))]
    public partial ProjectSummary Summary { get; set; } = ProjectSummary.Compute([], new HashSet<Guid>());

    public string OverallPercentText => $"{Summary.PercentComplete:F1}%";

    [ObservableProperty]
    public partial ISeries[] StatusSeries { get; set; } = Array.Empty<ISeries>();

    public ObservableCollection<DeliverableRow> DeliverableRows { get; } = new();

    public ObservableCollection<MilestoneRowViewModel> UpcomingMilestones { get; } = new();

    public DashboardViewModel(
        ProjectStateService projectStateService,
        WbsService wbsService,
        MilestoneService milestoneService,
        DialogService dialogService,
        ReportExportService reportExportService)
    {
        _projectStateService = projectStateService;
        _wbsService = wbsService;
        _milestoneService = milestoneService;
        _dialogService = dialogService;
        _reportExportService = reportExportService;
        _allAssignees = Strings.Get("Dashboard_AllAssignees");
    }

    public void OnNavigatedTo() => _ = LoadAsync();

    partial void OnSelectedAssigneeChanged(string? value)       => RebuildReport();
    partial void OnSelectedStatusChanged(WbsStatus? value)      => RebuildReport();
    partial void OnFilterStartDateChanged(DateTimeOffset? value) => RebuildReport();
    partial void OnFilterEndDateChanged(DateTimeOffset? value)   => RebuildReport();

    private async Task LoadAsync()
    {
        if (_projectStateService.ActiveProject is not { Id: var projectId }) return;
        try
        {
            IReadOnlyList<NodeDependency> dependencies =
                await _wbsService.GetAllDependenciesByProjectAsync(projectId);
            _allNodes   = await _wbsService.GetAllByProjectAsync(projectId);
            _blockedIds = DependencyConstraints.BlockedNodeIds(_allNodes, dependencies);

            RebuildAssigneeOptions();
            RebuildReport();
            _allMilestones = await _milestoneService.GetUpcomingAsync(projectId);
            RebuildMilestones();
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("ErrorTitle_Dashboard", ex);
        }
    }

    private void RebuildAssigneeOptions()
    {
        AssigneeOptions.Clear();
        AssigneeOptions.Add(_allAssignees);
        foreach (string a in _allNodes
            .Select(n => n.AssignedTo)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct()
            .OrderBy(a => a))
        {
            AssigneeOptions.Add(a);
        }
    }

    private void RebuildReport()
    {
        IEnumerable<WbsNode> filtered = _allNodes;

        if (!string.IsNullOrEmpty(SelectedAssignee) && SelectedAssignee != _allAssignees)
            filtered = filtered.Where(n => n.AssignedTo == SelectedAssignee);
        if (SelectedStatus.HasValue)
            filtered = filtered.Where(n => n.Status == SelectedStatus);
        if (FilterStartDate.HasValue)
            filtered = filtered.Where(n => n.DueDate.HasValue && n.DueDate.Value.Date >= FilterStartDate.Value.Date);
        if (FilterEndDate.HasValue)
            filtered = filtered.Where(n => n.DueDate.HasValue && n.DueDate.Value.Date <= FilterEndDate.Value.Date);

        List<WbsNode> nodes = filtered.ToList();
        Summary = ProjectSummary.Compute(nodes, _blockedIds);
        BuildStatusChart(Summary);
        BuildDeliverableRows(nodes);
        RebuildMilestones();
    }

    private void BuildStatusChart(ProjectSummary summary)
    {
        if (summary.TotalNodes == 0)
        {
            StatusSeries = Array.Empty<ISeries>();
            return;
        }

        int notStarted = Math.Max(0, summary.TotalNodes - summary.CompleteCount - summary.InProgressCount - summary.BlockedCount);
        StatusSeries =
        [
            Slice(WbsStatus.NotStarted, notStarted),
            Slice(WbsStatus.InProgress, summary.InProgressCount),
            Slice(WbsStatus.Complete,   summary.CompleteCount),
            Slice(WbsStatus.Blocked,    summary.BlockedCount),
        ];

        static ISeries Slice(WbsStatus status, int count) => new PieSeries<int>
        {
            Name = status.ToDisplayString(),
            Values = [count],
            Fill = new SolidColorPaint(StatusPalette.SkColor(status)),
            InnerRadius = 60
        };
    }

    private void BuildDeliverableRows(List<WbsNode> filteredNodes)
    {
        DeliverableRows.Clear();
        // Hierarchy and due dates come from all nodes; only progress honours the filter.
        HashSet<Guid> filteredIds = filteredNodes.Select(n => n.Id).ToHashSet();
        foreach (DeliverableRow row in DeliverableRow.Build(_allNodes, _blockedIds, filteredIds))
            DeliverableRows.Add(row);
    }

    private void RebuildMilestones()
    {
        UpcomingMilestones.Clear();

        // Only date filters apply to milestones; they have no assignee or status field.
        IEnumerable<Milestone> filtered = _allMilestones;
        if (FilterStartDate.HasValue)
            filtered = filtered.Where(m => m.DueDate.Date >= FilterStartDate.Value.Date);
        if (FilterEndDate.HasValue)
            filtered = filtered.Where(m => m.DueDate.Date <= FilterEndDate.Value.Date);

        foreach (Milestone m in filtered)
            UpcomingMilestones.Add(new MilestoneRowViewModel { Milestone = m });
    }

    [RelayCommand]
    private async Task ExportPdfAsync()
    {
        StorageFile? file = await FilePickers.PickStatusReportSaveFileAsync();
        if (file is null) return;
        try
        {
            await _reportExportService.ExportAsync(file.Path, ReportOptions.All);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("ErrorTitle_Export", ex);
            return;
        }
        await Launcher.LaunchFileAsync(file);
    }
}
