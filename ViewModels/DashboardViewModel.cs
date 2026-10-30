using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iscWBS.Core.Models;
using iscWBS.Core.Services;
using iscWBS.Helpers;

namespace iscWBS.ViewModels;

public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly ProjectStateService _projectStateService;
    private readonly WbsService _wbsService;
    private readonly MilestoneService _milestoneService;
    private readonly DialogService _dialogService;

    private IReadOnlyList<WbsNode> _allNodes = Array.Empty<WbsNode>();
    private IReadOnlySet<Guid> _blockedIds = new HashSet<Guid>();
    private IReadOnlyList<Milestone> _allMilestones = Array.Empty<Milestone>();

    [ObservableProperty]
    public partial DateTimeOffset? FilterStartDate { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset? FilterEndDate { get; set; }

    [ObservableProperty]
    public partial string? SelectedAssignee { get; set; }

    /// <summary>Set by clicking a status card; null shows every status.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotStartedFilter), nameof(IsInProgressFilter), nameof(IsCompleteFilter), nameof(IsBlockedFilter))]
    public partial WbsStatus? StatusFilter { get; set; }

    public bool IsNotStartedFilter => StatusFilter == WbsStatus.NotStarted;
    public bool IsInProgressFilter => StatusFilter == WbsStatus.InProgress;
    public bool IsCompleteFilter   => StatusFilter == WbsStatus.Complete;
    public bool IsBlockedFilter    => StatusFilter == WbsStatus.Blocked;

    public ObservableCollection<string> AssigneeOptions { get; } = new();

    private readonly string _allAssignees;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OverallPercentText), nameof(IsAllDone), nameof(NotStartedCount))]
    public partial ProjectSummary Summary { get; set; } = ProjectSummary.Compute([], new HashSet<Guid>());

    // Blocked only covers work that hasn't started, so the four statuses add up to the total.
    public int NotStartedCount => Summary.TotalNodes - Summary.CompleteCount - Summary.InProgressCount - Summary.BlockedCount;

    public bool IsAllDone => Summary.TotalNodes > 0 && Summary.CompleteCount == Summary.TotalNodes;

    public string OverallPercentText => $"{Summary.PercentComplete:F1}%";

    [ObservableProperty]
    public partial List<StatusSlice> StatusSlices { get; set; } = [];

    public ObservableCollection<DeliverableRow> DeliverableRows { get; } = new();

    public bool HasNoDeliverables => !_allNodes.Any(n => n.IsDeliverable);

    public bool HasNoMatchingDeliverables => !HasNoDeliverables && DeliverableRows.Count == 0;

    public ObservableCollection<MilestoneRowViewModel> UpcomingMilestones { get; } = new();

    public bool HasNoUpcomingMilestones => UpcomingMilestones.Count == 0;

    public string MilestonesTitle => FilterStartDate.HasValue || FilterEndDate.HasValue
        ? Strings.Get("Dashboard_MilestonesInDates")
        : Strings.Format("Dashboard_UpcomingMilestones", MilestoneService.UpcomingDays);

    public DashboardViewModel(
        ProjectStateService projectStateService,
        WbsService wbsService,
        MilestoneService milestoneService,
        DialogService dialogService)
    {
        _projectStateService = projectStateService;
        _wbsService = wbsService;
        _milestoneService = milestoneService;
        _dialogService = dialogService;
        _allAssignees = Strings.Get("Dashboard_FilterAll");
    }

    public void OnNavigatedTo() => _ = LoadAsync();

    /// <summary>The project has nodes, but the filters exclude all of them.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMatches))]
    public partial bool NothingMatchesFilters { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMatches), nameof(HasNodes))]
    public partial bool IsEmptyProject { get; set; }

    public bool HasNodes => !IsEmptyProject;

    public bool HasMatches => !NothingMatchesFilters && !IsEmptyProject;

    [RelayCommand]
    private void ClearFilters()
    {
        FilterStartDate = null;
        FilterEndDate = null;
        SelectedAssignee = null;
        StatusFilter = null;
    }

    /// <summary>Filters to <paramref name="status"/>, or back to every status when it is already the filter.</summary>
    [RelayCommand]
    private void ToggleStatusFilter(string status)
    {
        WbsStatus picked = Enum.Parse<WbsStatus>(status);
        StatusFilter = StatusFilter == picked ? null : picked;
    }

    partial void OnSelectedAssigneeChanged(string? value)       => RebuildReport();
    partial void OnStatusFilterChanged(WbsStatus? value) => RebuildReport();
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
            _allMilestones = [.. (await _milestoneService.GetByProjectAsync(projectId)).Where(m => !m.IsComplete)];
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
        if (FilterStartDate.HasValue)
            filtered = filtered.Where(n => n.DueDate.HasValue && n.DueDate.Value.Date >= FilterStartDate.Value.Date);
        if (FilterEndDate.HasValue)
            filtered = filtered.Where(n => n.DueDate.HasValue && n.DueDate.Value.Date <= FilterEndDate.Value.Date);

        // The cards and the chart leave the status filter out: they are what it picks from, and under it every
        // other status would count 0.
        List<WbsNode> nodes = filtered.ToList();
        IsEmptyProject = _allNodes.Count == 0;
        NothingMatchesFilters = nodes.Count == 0 && _allNodes.Count > 0;
        Summary = ProjectSummary.Compute(nodes, _blockedIds);
        BuildStatusChart(Summary);
        // The status shown everywhere else, so a node held up by a dependency counts as Blocked, not Not Started.
        BuildDeliverableRows(StatusFilter is { } status
            ? nodes.Where(n => DependencyConstraints.EffectiveStatus(n, _blockedIds) == status).ToList()
            : nodes);
        RebuildMilestones();
    }

    private void BuildStatusChart(ProjectSummary summary)
    {
        StatusSlices = summary.TotalNodes == 0 ? [] :
        [
            new(WbsStatus.NotStarted, NotStartedCount),
            new(WbsStatus.InProgress, summary.InProgressCount),
            new(WbsStatus.Complete,   summary.CompleteCount),
            new(WbsStatus.Blocked,    summary.BlockedCount),
        ];
    }

    private void BuildDeliverableRows(List<WbsNode> filteredNodes)
    {
        DeliverableRows.Clear();
        // A deliverable shows when something in it matches the filters. Matching on a task's status alone would list an
        // in-progress deliverable under Complete, so the deliverable's own status has to match too.
        HashSet<Guid> matchingIds = filteredNodes.Select(n => n.Id).ToHashSet();
        WbsStatus? status = StatusFilter;
        foreach (DeliverableRow row in DeliverableRow.Build(_allNodes, _blockedIds, matchingIds))
        {
            if (status is null || row.Status == status)
                DeliverableRows.Add(row);
        }
        OnPropertyChanged(nameof(HasNoDeliverables));
        OnPropertyChanged(nameof(HasNoMatchingDeliverables));
    }

    private void RebuildMilestones()
    {
        UpcomingMilestones.Clear();

        // Only date filters apply to milestones; they have no assignee or status field. Without them the list
        // is the coming weeks, overdue ones included.
        OnPropertyChanged(nameof(MilestonesTitle));
        IEnumerable<Milestone> filtered = _allMilestones;
        if (FilterStartDate.HasValue)
            filtered = filtered.Where(m => m.DueDate.Date >= FilterStartDate.Value.Date);
        if (FilterEndDate.HasValue)
            filtered = filtered.Where(m => m.DueDate.Date <= FilterEndDate.Value.Date);
        else if (!FilterStartDate.HasValue)
            filtered = filtered.Where(m => m.DueDate.Date <= DateTime.Today.AddDays(MilestoneService.UpcomingDays));

        foreach (Milestone m in filtered)
            UpcomingMilestones.Add(new MilestoneRowViewModel { Milestone = m });
        OnPropertyChanged(nameof(HasNoUpcomingMilestones));
    }

}

// Get-only properties: the XAML type metadata generator emits setters for init-only ones, which don't compile.
public sealed class StatusSlice(WbsStatus status, int count)
{
    public WbsStatus Status { get; } = status;
    public int Count { get; } = count;
    public string Label => $"{Status.ToDisplayString()} ({Count})";
    public override string ToString() => Label;
}
