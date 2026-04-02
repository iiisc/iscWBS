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
using iscWBS.Core.Services;

namespace iscWBS.ViewModels;

public sealed partial class ReportsViewModel : ObservableObject, INavigationAware
{
    private readonly IProjectStateService _projectStateService;
    private readonly IWbsService _wbsService;
    private readonly IDialogService _dialogService;

    private IReadOnlyList<WbsNode> _allNodes = Array.Empty<WbsNode>();

    [ObservableProperty]
    public partial ISeries[] ProgressSeries { get; set; } = Array.Empty<ISeries>();

    [ObservableProperty]
    public partial IEnumerable<ICartesianAxis> ProgressXAxes { get; set; } = Array.Empty<ICartesianAxis>();

    [ObservableProperty]
    public partial ISeries[] BurnDownSeries { get; set; } = Array.Empty<ISeries>();

    [ObservableProperty]
    public partial IEnumerable<ICartesianAxis> BurnDownXAxes { get; set; } = Array.Empty<ICartesianAxis>();

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

    public ReportsViewModel(
        IProjectStateService projectStateService,
        IWbsService wbsService,
        IDialogService dialogService)
    {
        _projectStateService = projectStateService;
        _wbsService = wbsService;
        _dialogService = dialogService;
    }

    public void OnNavigatedTo(object? parameter) => _ = LoadAsync();
    public void OnNavigatedFrom() { }

    partial void OnSelectedAssigneeChanged(string? value) => RebuildCharts();
    partial void OnSelectedStatusChanged(WbsStatus? value) => RebuildCharts();
    partial void OnFilterStartDateChanged(DateTimeOffset? value) => RebuildCharts();
    partial void OnFilterEndDateChanged(DateTimeOffset? value) => RebuildCharts();

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (_projectStateService.ActiveProject is not { Id: var projectId }) return;
        try
        {
            _allNodes = await _wbsService.GetAllByProjectAsync(projectId);
            RebuildAssigneeOptions();
            RebuildCharts();
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Reports Error", ex.Message);
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

    private void RebuildCharts()
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
        BuildProgressChart(nodes);
        BuildBurnDownChart(nodes);
    }

    private void BuildProgressChart(List<WbsNode> nodes)
    {
        if (nodes.Count == 0)
        {
            ProgressSeries = Array.Empty<ISeries>();
            ProgressXAxes = Array.Empty<ICartesianAxis>();
            return;
        }

        int total = nodes.Count;
        int complete = nodes.Count(n => n.Status == WbsStatus.Complete);
        int inProgress = nodes.Count(n => n.Status == WbsStatus.InProgress);
        double remaining = total - complete - inProgress;

        ProgressSeries = new ISeries[]
        {
            new ColumnSeries<double>
            {
                Name = "Complete",
                Values = new[] { (double)complete },
                Fill = new SolidColorPaint(new SKColor(0x10, 0x7C, 0x10, 0xCC))
            },
            new ColumnSeries<double>
            {
                Name = "In Progress",
                Values = new[] { (double)inProgress },
                Fill = new SolidColorPaint(new SKColor(0x00, 0x78, 0xD4, 0xCC))
            },
            new ColumnSeries<double>
            {
                Name = "Remaining",
                Values = new[] { remaining },
                Fill = new SolidColorPaint(new SKColor(0x80, 0x80, 0x80, 0xCC))
            }
        };
        ProgressXAxes = new[] { new Axis { Labels = new[] { "Nodes" } } };
    }

    private void BuildBurnDownChart(List<WbsNode> nodes)
    {
        if (nodes.Count == 0)
        {
            BurnDownSeries = Array.Empty<ISeries>();
            BurnDownXAxes = Array.Empty<ICartesianAxis>();
            return;
        }

        double totalHours = nodes.Sum(n => n.EstimatedHours);
        double actualHours = nodes.Sum(n => n.ActualHours);
        double remaining = Math.Max(0, totalHours - actualHours);

        DateTime? start = _projectStateService.ActiveProject?.StartDate
            ?? nodes.Where(n => n.StartDate.HasValue).Select(n => n.StartDate!.Value).DefaultIfEmpty(DateTime.Today.AddMonths(-1)).Min();
        DateTime? end = nodes.Where(n => n.DueDate.HasValue).Select(n => n.DueDate!.Value).DefaultIfEmpty(DateTime.Today.AddMonths(1)).Max();

        if (start is null || end is null || end <= start)
        {
            BurnDownSeries = Array.Empty<ISeries>();
            BurnDownXAxes = Array.Empty<ICartesianAxis>();
            return;
        }

        var idealPoints = new DateTimePoint[]
        {
            new(start.Value, totalHours),
            new(end.Value, 0)
        };

        var remainingPoints = new DateTimePoint[]
        {
            new(start.Value, totalHours),
            new(DateTime.Today, remaining)
        };

        BurnDownSeries = new ISeries[]
        {
            new LineSeries<DateTimePoint>
            {
                Name = "Ideal",
                Values = idealPoints,
                GeometryFill = null,
                GeometryStroke = null,
                Stroke = new SolidColorPaint(new SKColor(0x80, 0x80, 0x80), 2)
            },
            new LineSeries<DateTimePoint>
            {
                Name = "Remaining",
                Values = remainingPoints,
                GeometryFill = null,
                GeometryStroke = null,
                Stroke = new SolidColorPaint(new SKColor(0xC5, 0x0F, 0x1F), 2)
            }
        };

        BurnDownXAxes = new ICartesianAxis[] { new DateTimeAxis(TimeSpan.FromDays(30), d => d.ToString("MMM yy")) };
    }
}

