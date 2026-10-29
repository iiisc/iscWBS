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

    [ObservableProperty]
    public partial StatusFilterOption? SelectedStatus { get; set; }

    public ObservableCollection<string> AssigneeOptions { get; } = new();

    private readonly string _allAssignees;

    public List<StatusFilterOption> StatusOptions { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OverallPercentText))]
    public partial ProjectSummary Summary { get; set; } = ProjectSummary.Compute([], new HashSet<Guid>());

    public string OverallPercentText => $"{Summary.PercentComplete:F1}%";

    [ObservableProperty]
    public partial List<StatusSlice> StatusSlices { get; set; } = [];

    public ObservableCollection<DeliverableRow> DeliverableRows { get; } = new();

    public ObservableCollection<MilestoneRowViewModel> UpcomingMilestones { get; } = new();

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
        StatusOptions = [new(null, _allAssignees), .. Enum.GetValues<WbsStatus>().Select(s => new StatusFilterOption(s, s.ToDisplayString()))];
    }

    public void OnNavigatedTo() => _ = LoadAsync();

    /// <summary>The project has nodes, but the filters exclude all of them.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMatches))]
    public partial bool NothingMatchesFilters { get; set; }

    public bool HasMatches => !NothingMatchesFilters;

    [RelayCommand]
    private void ClearFilters()
    {
        FilterStartDate = null;
        FilterEndDate = null;
        SelectedAssignee = null;
        SelectedStatus = null;
    }

    partial void OnSelectedAssigneeChanged(string? value)       => RebuildReport();
    partial void OnSelectedStatusChanged(StatusFilterOption? value) => RebuildReport();
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
        if (SelectedStatus?.Status is { } status)
            filtered = filtered.Where(n => n.Status == status);
        if (FilterStartDate.HasValue)
            filtered = filtered.Where(n => n.DueDate.HasValue && n.DueDate.Value.Date >= FilterStartDate.Value.Date);
        if (FilterEndDate.HasValue)
            filtered = filtered.Where(n => n.DueDate.HasValue && n.DueDate.Value.Date <= FilterEndDate.Value.Date);

        List<WbsNode> nodes = filtered.ToList();
        NothingMatchesFilters = nodes.Count == 0 && _allNodes.Count > 0;
        Summary = ProjectSummary.Compute(nodes, _blockedIds);
        BuildStatusChart(Summary);
        BuildDeliverableRows(nodes);
        RebuildMilestones();
    }

    private void BuildStatusChart(ProjectSummary summary)
    {
        int notStarted = Math.Max(0, summary.TotalNodes - summary.CompleteCount - summary.InProgressCount - summary.BlockedCount);
        StatusSlices = summary.TotalNodes == 0 ? [] :
        [
            new(WbsStatus.NotStarted, notStarted),
            new(WbsStatus.InProgress, summary.InProgressCount),
            new(WbsStatus.Complete,   summary.CompleteCount),
            new(WbsStatus.Blocked,    summary.BlockedCount),
        ];
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

}

// Get-only properties: the XAML type metadata generator emits setters for init-only ones, which don't compile.
public sealed class StatusSlice(WbsStatus status, int count)
{
    public WbsStatus Status { get; } = status;
    public int Count { get; } = count;
    public string Label => $"{Status.ToDisplayString()} ({Count})";
}

public sealed class StatusFilterOption(WbsStatus? status, string label)
{
    public WbsStatus? Status { get; } = status;
    public string Label { get; } = label;
}
