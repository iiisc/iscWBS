using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using iscWBS.Core.Models;
using iscWBS.Core.Services;
using iscWBS.Helpers;

namespace iscWBS.ViewModels;

public sealed partial class DashboardViewModel : ObservableObject, INavigationAware
{
    private readonly IProjectStateService _projectStateService;
    private readonly IWbsService _wbsService;
    private readonly IMilestoneService _milestoneService;
    private readonly IDialogService _dialogService;
    private readonly IReportExportService _reportExportService;

    private IReadOnlyList<WbsNode> _allNodes = Array.Empty<WbsNode>();
    private IReadOnlySet<Guid> _blockedIds = new HashSet<Guid>();
    private IReadOnlyList<Milestone> _allMilestones = Array.Empty<Milestone>();

    // ─── Filters ──────────────────────────────────────────────────────────────

    [ObservableProperty]
    public partial DateTimeOffset? FilterStartDate { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset? FilterEndDate { get; set; }

    [ObservableProperty]
    public partial string? SelectedAssignee { get; set; }

    [ObservableProperty]
    public partial WbsStatus? SelectedStatus { get; set; }

    public ObservableCollection<string> AssigneeOptions { get; } = new();

    public IReadOnlyList<WbsStatus?> StatusOptions { get; } =
        new WbsStatus?[] { null }
            .Concat(Enum.GetValues<WbsStatus>().Cast<WbsStatus?>())
            .ToList();

    // ─── KPI strip ────────────────────────────────────────────────────────────

    [ObservableProperty]
    public partial int CompleteCount { get; set; }

    [ObservableProperty]
    public partial int InProgressCount { get; set; }

    [ObservableProperty]
    public partial int AtRiskCount { get; set; }

    [ObservableProperty]
    public partial int OverdueCount { get; set; }

    [ObservableProperty]
    public partial int BlockedCount { get; set; }

    [ObservableProperty]
    public partial double OverallPercent { get; set; }

    /// <summary>Formatted overall percent, e.g. "42.1%".</summary>
    public string OverallPercentText => $"{OverallPercent:F1}%";

    partial void OnOverallPercentChanged(double value) => OnPropertyChanged(nameof(OverallPercentText));

    // ─── Status donut chart ───────────────────────────────────────────────────

    [ObservableProperty]
    public partial ISeries[] StatusSeries { get; set; } = Array.Empty<ISeries>();

    // ─── Deliverables table ───────────────────────────────────────────────────

    public ObservableCollection<DeliverableRow> DeliverableRows { get; } = new();

    // ─── Upcoming milestones ──────────────────────────────────────────────────

    public ObservableCollection<MilestoneRowViewModel> UpcomingMilestones { get; } = new();

    // ─── Export state ─────────────────────────────────────────────────────────

    [ObservableProperty]
    public partial bool IsExporting { get; set; }

    public DashboardViewModel(
        IProjectStateService projectStateService,
        IWbsService wbsService,
        IMilestoneService milestoneService,
        IDialogService dialogService,
        IReportExportService reportExportService)
    {
        _projectStateService = projectStateService;
        _wbsService = wbsService;
        _milestoneService = milestoneService;
        _dialogService = dialogService;
        _reportExportService = reportExportService;
    }

    public void OnNavigatedTo(object? parameter) => _ = LoadAsync();
    public void OnNavigatedFrom() { }

    partial void OnSelectedAssigneeChanged(string? value)       => RebuildReport();
    partial void OnSelectedStatusChanged(WbsStatus? value)      => RebuildReport();
    partial void OnFilterStartDateChanged(DateTimeOffset? value) => RebuildReport();
    partial void OnFilterEndDateChanged(DateTimeOffset? value)   => RebuildReport();

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (_projectStateService.ActiveProject is not { Id: var projectId }) return;
        try
        {
            IReadOnlyList<NodeDependency> dependencies =
                await _wbsService.GetAllDependenciesByProjectAsync(projectId);
            _allNodes   = await _wbsService.GetAllByProjectAsync(projectId);
            _blockedIds = _wbsService.ResolveBlockedNodeIds(_allNodes, dependencies);

            RebuildAssigneeOptions();
            RebuildReport();
            await LoadMilestonesAsync(projectId);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Dashboard Error", ex.Message);
        }
    }

    private void RebuildAssigneeOptions()
    {
        AssigneeOptions.Clear();
        AssigneeOptions.Add("All");
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

        if (!string.IsNullOrEmpty(SelectedAssignee) && SelectedAssignee != "All")
            filtered = filtered.Where(n => n.AssignedTo == SelectedAssignee);
        if (SelectedStatus.HasValue)
            filtered = filtered.Where(n => n.Status == SelectedStatus);
        if (FilterStartDate.HasValue)
            filtered = filtered.Where(n => n.DueDate.HasValue && n.DueDate.Value >= FilterStartDate.Value.UtcDateTime);
        if (FilterEndDate.HasValue)
            filtered = filtered.Where(n => n.DueDate.HasValue && n.DueDate.Value <= FilterEndDate.Value.UtcDateTime);

        List<WbsNode> nodes = filtered.ToList();
        BuildKpis(nodes);
        BuildStatusChart(nodes);
        BuildDeliverableRows(nodes);
        RebuildMilestones();
    }

    private void BuildKpis(List<WbsNode> nodes)
    {
        int total    = nodes.Count;
        int complete = nodes.Count(n => n.Status == WbsStatus.Complete);

        OverallPercent  = total > 0 ? Math.Round((double)complete / total * 100, 1) : 0;
        CompleteCount   = complete;
        InProgressCount = nodes.Count(n => n.Status == WbsStatus.InProgress);
        BlockedCount    = nodes.Count(n => _blockedIds.Contains(n.Id) || n.Status == WbsStatus.Blocked);
        OverdueCount    = nodes.Count(n =>
            n.Status != WbsStatus.Complete &&
            n.DueDate.HasValue &&
            n.DueDate.Value.Date < DateTime.Today);
        AtRiskCount = nodes.Count(n =>
            n.Status != WbsStatus.Complete &&
            n.DueDate.HasValue &&
            n.DueDate.Value.Date >= DateTime.Today &&
            n.DueDate.Value.Date <= DateTime.Today.AddDays(14));
    }

    private void BuildStatusChart(List<WbsNode> nodes)
    {
        if (nodes.Count == 0)
        {
            StatusSeries = Array.Empty<ISeries>();
            return;
        }

        int complete   = nodes.Count(n => n.Status == WbsStatus.Complete);
        int inProgress = nodes.Count(n => n.Status == WbsStatus.InProgress);
        int blocked    = nodes.Count(n => _blockedIds.Contains(n.Id) || n.Status == WbsStatus.Blocked);
        int notStarted = Math.Max(0, nodes.Count - complete - inProgress - blocked);

        StatusSeries = new ISeries[]
        {
            new PieSeries<int> { Name = "Not Started", Values = new[] { notStarted }, Fill = new SolidColorPaint(ChartPalette.NotStarted), InnerRadius = 60 },
            new PieSeries<int> { Name = "In Progress", Values = new[] { inProgress }, Fill = new SolidColorPaint(ChartPalette.InProgress), InnerRadius = 60 },
            new PieSeries<int> { Name = "Complete",    Values = new[] { complete },   Fill = new SolidColorPaint(ChartPalette.Complete),   InnerRadius = 60 },
            new PieSeries<int> { Name = "Blocked",     Values = new[] { blocked },    Fill = new SolidColorPaint(ChartPalette.Blocked),    InnerRadius = 60 },
        };
    }

    private void BuildDeliverableRows(List<WbsNode> filteredNodes)
    {
        DeliverableRows.Clear();

        // Full hierarchy map — always from _allNodes so parent/child structure is intact.
        Dictionary<Guid, List<WbsNode>> childMap = _allNodes
            .Where(n => n.ParentId.HasValue)
            .GroupBy(n => n.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(n => n.SortOrder).ToList());

        // IDs that survived the current filter — used to count progress within each deliverable.
        HashSet<Guid> filteredIds = filteredNodes.Select(n => n.Id).ToHashSet();

        IEnumerable<WbsNode> deliverableNodes = _allNodes
            .Where(n => n.IsDeliverable)
            .OrderBy(n => n.SortOrder);

        foreach (WbsNode root in deliverableNodes)
        {
            List<WbsNode> fullSubtree = CollectSubtree(root, childMap);

            // Progress counts only filtered tasks within this deliverable's subtree.
            List<WbsNode> countedSubtree = fullSubtree
                .Where(n => filteredIds.Contains(n.Id))
                .ToList();

            int total    = countedSubtree.Count;
            int complete = countedSubtree.Count(n => n.Status == WbsStatus.Complete);
            double fraction = total > 0 ? (double)complete / total : 0;

            // Due dates always from the full subtree — timeline doesn't change with filter.
            List<DateTime> dueDates = fullSubtree
                .Where(n => n.DueDate.HasValue)
                .Select(n => n.DueDate!.Value)
                .ToList();
            DateTime? latestDue = dueDates.Count > 0 ? dueDates.Max() : null;

            bool isOverdue = latestDue.HasValue &&
                latestDue.Value.Date < DateTime.Today &&
                root.Status != WbsStatus.Complete;
            bool isAtRisk = latestDue.HasValue &&
                latestDue.Value.Date >= DateTime.Today &&
                latestDue.Value.Date <= DateTime.Today.AddDays(14) &&
                root.Status != WbsStatus.Complete;

            WbsStatus effectiveStatus = _blockedIds.Contains(root.Id)
                ? WbsStatus.Blocked
                : root.Status;

            DeliverableRows.Add(new DeliverableRow(
                root.Code, root.Title, total, complete, fraction,
                effectiveStatus, latestDue, isOverdue, isAtRisk));
        }
    }

    private static List<WbsNode> CollectSubtree(WbsNode root, Dictionary<Guid, List<WbsNode>> childMap)
    {
        var result = new List<WbsNode> { root };
        if (childMap.TryGetValue(root.Id, out List<WbsNode>? children))
            foreach (WbsNode child in children)
                result.AddRange(CollectSubtree(child, childMap));
        return result;
    }

    private async Task LoadMilestonesAsync(Guid projectId)
    {
        _allMilestones = await _milestoneService.GetUpcomingAsync(projectId, 60);
        RebuildMilestones();
    }

    private void RebuildMilestones()
    {
        UpcomingMilestones.Clear();

        // Only date filters apply to milestones; they have no assignee or status field.
        IEnumerable<Milestone> filtered = _allMilestones;
        if (FilterStartDate.HasValue)
            filtered = filtered.Where(m => m.DueDate >= FilterStartDate.Value.UtcDateTime);
        if (FilterEndDate.HasValue)
            filtered = filtered.Where(m => m.DueDate <= FilterEndDate.Value.UtcDateTime);

        foreach (Milestone m in filtered)
        {
            UpcomingMilestones.Add(new MilestoneRowViewModel
            {
                Milestone       = m,
                DueDateLabel    = m.DueDate.ToString("dd MMM yyyy"),
                LinkedNodeCount = 0
            });
        }
    }

    /// <summary>
    /// Generates a PDF report and saves it to <paramref name="filePath"/>.
    /// Returns <see langword="true"/> on success; errors are surfaced via <see cref="IDialogService"/>.
    /// </summary>
    public async Task<bool> ExportPdfAsync(string filePath)
    {
        if (IsExporting) return false;
        IsExporting = true;
        try
        {
            await _reportExportService.ExportAsync(filePath, ReportOptions.All);
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

