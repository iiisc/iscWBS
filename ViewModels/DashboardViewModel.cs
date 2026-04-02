using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using iscWBS.Core.Models;
using iscWBS.Core.Repositories;
using iscWBS.Core.Services;

namespace iscWBS.ViewModels;

public sealed partial class DashboardViewModel : ObservableObject, INavigationAware
{
    private readonly IProjectStateService _projectStateService;
    private readonly IWbsService _wbsService;
    private readonly MilestoneRepository _milestoneRepository;
    private readonly IDialogService _dialogService;

    [ObservableProperty]
    public partial int TotalNodes { get; set; }

    [ObservableProperty]
    public partial double PercentComplete { get; set; }

    [ObservableProperty]
    public partial double BudgetVariance { get; set; }

    [ObservableProperty]
    public partial int OverdueCount { get; set; }

    public string BudgetVarianceDisplay => BudgetVariance >= 0
        ? $"+{BudgetVariance:C0}"
        : $"{BudgetVariance:C0}";

    [ObservableProperty]
    public partial ISeries[] StatusSeries { get; set; } = Array.Empty<ISeries>();

    [ObservableProperty]
    public partial ISeries[] CostSeries { get; set; } = Array.Empty<ISeries>();

    [ObservableProperty]
    public partial IEnumerable<ICartesianAxis> CostXAxes { get; set; } = Array.Empty<ICartesianAxis>();

    [ObservableProperty]
    public partial ISeries[] EffortSeries { get; set; } = Array.Empty<ISeries>();

    [ObservableProperty]
    public partial IEnumerable<ICartesianAxis> EffortXAxes { get; set; } = Array.Empty<ICartesianAxis>();

    public ObservableCollection<Milestone> UpcomingMilestones { get; } = new();

    public DashboardViewModel(
        IProjectStateService projectStateService,
        IWbsService wbsService,
        MilestoneRepository milestoneRepository,
        IDialogService dialogService)
    {
        _projectStateService = projectStateService;
        _wbsService = wbsService;
        _milestoneRepository = milestoneRepository;
        _dialogService = dialogService;
    }

    public void OnNavigatedTo(object? parameter) => _ = LoadAsync();
    public void OnNavigatedFrom() { }

    partial void OnBudgetVarianceChanged(double value)
        => OnPropertyChanged(nameof(BudgetVarianceDisplay));

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (_projectStateService.ActiveProject is not { Id: var projectId }) return;
        try
        {
            IReadOnlyList<WbsNode> nodes = await _wbsService.GetAllByProjectAsync(projectId);
            IReadOnlyList<WbsNode> roots = await _wbsService.GetRootNodesAsync(projectId);

            BuildKpis(nodes);
            BuildStatusChart(nodes);
            BuildCostChart(roots);
            BuildEffortChart(nodes);
            await LoadMilestonesAsync(projectId);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Dashboard Error", ex.Message);
        }
    }

    private void BuildKpis(IReadOnlyList<WbsNode> nodes)
    {
        TotalNodes = nodes.Count;
        int complete = nodes.Count(n => n.Status == WbsStatus.Complete);
        PercentComplete = TotalNodes == 0 ? 0 : Math.Round(complete * 100.0 / TotalNodes, 1);
        BudgetVariance = nodes.Sum(n => n.EstimatedCost) - nodes.Sum(n => n.ActualCost);
        OverdueCount = nodes.Count(n =>
            n.DueDate.HasValue &&
            n.DueDate.Value < DateTime.UtcNow &&
            n.Status != WbsStatus.Complete);
    }

    private void BuildStatusChart(IReadOnlyList<WbsNode> nodes)
    {
        StatusSeries = new ISeries[]
        {
            new PieSeries<ObservableValue>
            {
                Name = "Not Started",
                Values = new[] { new ObservableValue(nodes.Count(n => n.Status == WbsStatus.NotStarted)) },
                Fill = new SolidColorPaint(new SKColor(0x80, 0x80, 0x80))
            },
            new PieSeries<ObservableValue>
            {
                Name = "In Progress",
                Values = new[] { new ObservableValue(nodes.Count(n => n.Status == WbsStatus.InProgress)) },
                Fill = new SolidColorPaint(new SKColor(0x00, 0x78, 0xD4))
            },
            new PieSeries<ObservableValue>
            {
                Name = "Complete",
                Values = new[] { new ObservableValue(nodes.Count(n => n.Status == WbsStatus.Complete)) },
                Fill = new SolidColorPaint(new SKColor(0x10, 0x7C, 0x10))
            },
            new PieSeries<ObservableValue>
            {
                Name = "Blocked",
                Values = new[] { new ObservableValue(nodes.Count(n => n.Status == WbsStatus.Blocked)) },
                Fill = new SolidColorPaint(new SKColor(0xC5, 0x0F, 0x1F))
            },
        };
    }

    private void BuildCostChart(IReadOnlyList<WbsNode> roots)
    {
        if (roots.Count == 0)
        {
            CostSeries = Array.Empty<ISeries>();
            CostXAxes = Array.Empty<ICartesianAxis>();
            return;
        }

        CostSeries = new ISeries[]
        {
            new ColumnSeries<double>
            {
                Name = "Estimated",
                Values = roots.Select(r => r.EstimatedCost).ToArray(),
                Fill = new SolidColorPaint(new SKColor(0x00, 0x78, 0xD4, 0xCC))
            },
            new ColumnSeries<double>
            {
                Name = "Actual",
                Values = roots.Select(r => r.ActualCost).ToArray(),
                Fill = new SolidColorPaint(new SKColor(0x10, 0x7C, 0x10, 0xCC))
            }
        };
        CostXAxes = new[] { new Axis { Labels = roots.Select(r => r.Code).ToArray() } };
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
                Fill = new SolidColorPaint(new SKColor(0x00, 0x78, 0xD4, 0xCC))
            },
            new ColumnSeries<double>
            {
                Name = "Act. Hours",
                Values = byAssignee.Select(g => g.Sum(n => n.ActualHours)).ToArray(),
                Fill = new SolidColorPaint(new SKColor(0x10, 0x7C, 0x10, 0xCC))
            }
        };
        EffortXAxes = new[] { new Axis { Labels = labels } };
    }

    private async Task LoadMilestonesAsync(Guid projectId)
    {
        UpcomingMilestones.Clear();
        IReadOnlyList<Milestone> milestones = await _milestoneRepository.GetUpcomingAsync(projectId);
        foreach (Milestone m in milestones)
            UpcomingMilestones.Add(m);
    }
}

