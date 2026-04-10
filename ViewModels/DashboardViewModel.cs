using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Kernel.Sketches;
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

    [ObservableProperty]
    public partial int TotalNodes { get; set; }

    [ObservableProperty]
    public partial double PercentComplete { get; set; }

    [ObservableProperty]
    public partial int OverdueCount { get; set; }

    [ObservableProperty]
    public partial ISeries[] StatusSeries { get; set; } = Array.Empty<ISeries>();

    [ObservableProperty]
    public partial ISeries[] EffortSeries { get; set; } = Array.Empty<ISeries>();

    [ObservableProperty]
    public partial IEnumerable<ICartesianAxis> EffortXAxes { get; set; } = Array.Empty<ICartesianAxis>();

    public ObservableCollection<Milestone> UpcomingMilestones { get; } = new();

    public DashboardViewModel(
        IProjectStateService projectStateService,
        IWbsService wbsService,
        IMilestoneService milestoneService,
        IDialogService dialogService)
    {
        _projectStateService = projectStateService;
        _wbsService = wbsService;
        _milestoneService = milestoneService;
        _dialogService = dialogService;
    }

    public void OnNavigatedTo(object? parameter) => _ = LoadAsync();
    public void OnNavigatedFrom() { }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (_projectStateService.ActiveProject is not { Id: var projectId }) return;
        try
        {
            IReadOnlyList<WbsNode> nodes = await _wbsService.GetAllByProjectAsync(projectId);
            IReadOnlyList<NodeDependency> dependencies =
                await _wbsService.GetAllDependenciesByProjectAsync(projectId);
            IReadOnlySet<Guid> blockedIds = _wbsService.ResolveBlockedNodeIds(nodes, dependencies);

                ProjectSummary summary = _wbsService.ComputeProjectSummary(nodes, blockedIds);
                TotalNodes      = summary.TotalNodes;
                PercentComplete = summary.PercentComplete;
                OverdueCount    = summary.OverdueCount;
                BuildStatusChart(summary);
                BuildEffortChart(nodes);
            await LoadMilestonesAsync(projectId);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Dashboard Error", ex.Message);
        }
    }

    private void BuildStatusChart(ProjectSummary summary)
    {
        StatusSeries = new ISeries[]
        {
            new PieSeries<ObservableValue>
            {
                Name = "Not Started",
                Values = new[] { new ObservableValue(summary.NotStartedCount) },
                Fill = new SolidColorPaint(ChartPalette.NotStarted)
            },
            new PieSeries<ObservableValue>
            {
                Name = "In Progress",
                Values = new[] { new ObservableValue(summary.InProgressCount) },
                Fill = new SolidColorPaint(ChartPalette.InProgress)
            },
            new PieSeries<ObservableValue>
            {
                Name = "Complete",
                Values = new[] { new ObservableValue(summary.CompleteCount) },
                Fill = new SolidColorPaint(ChartPalette.Complete)
            },
            new PieSeries<ObservableValue>
            {
                Name = "Blocked",
                Values = new[] { new ObservableValue(summary.BlockedCount) },
                Fill = new SolidColorPaint(ChartPalette.Blocked)
            },
        };
    }

    private void BuildEffortChart(IReadOnlyList<WbsNode> nodes)
    {
        var byAssignee = nodes
            .Where(n => !string.IsNullOrWhiteSpace(n.AssignedTo))
            .GroupBy(n => n.AssignedTo)
            .OrderBy(g => g.Key)
            .ToList();

        if (byAssignee.Count == 0)
        {
            EffortSeries = Array.Empty<ISeries>();
            EffortXAxes = Array.Empty<ICartesianAxis>();
            return;
        }

        string[] labels = byAssignee.Select(g => g.Key).ToArray();
        EffortSeries = new ISeries[]
        {
            new ColumnSeries<double>
            {
                Name = "Est. Hours",
                Values = byAssignee.Select(g => g.Sum(n => n.EstimatedHours)).ToArray(),
                Fill = new SolidColorPaint(ChartPalette.InProgressAlpha)
            },
            new ColumnSeries<double>
            {
                Name = "Act. Hours",
                Values = byAssignee.Select(g => g.Sum(n => n.ActualHours)).ToArray(),
                Fill = new SolidColorPaint(ChartPalette.CompleteAlpha)
            }
        };
        EffortXAxes = new[] { new Axis { Labels = labels } };
    }

    private async Task LoadMilestonesAsync(Guid projectId)
    {
        UpcomingMilestones.Clear();
        IReadOnlyList<Milestone> milestones = await _milestoneService.GetUpcomingAsync(projectId);
        foreach (Milestone m in milestones)
            UpcomingMilestones.Add(m);
    }
}

