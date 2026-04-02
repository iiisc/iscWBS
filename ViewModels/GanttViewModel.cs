using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iscWBS.Core.Models;
using iscWBS.Core.Services;

namespace iscWBS.ViewModels;

public sealed partial class GanttViewModel : ObservableObject, INavigationAware
{
    private readonly IProjectStateService _projectStateService;
    private readonly IWbsService _wbsService;
    private readonly IDialogService _dialogService;

    private static readonly double[] _pixelsPerDayByZoom = { 40.0, 8.0, 2.0 };

    [ObservableProperty]
    public partial int SelectedZoomIndex { get; set; }

    [ObservableProperty]
    public partial double TotalWidth { get; set; }

    [ObservableProperty]
    public partial double TotalHeight { get; set; }

    [ObservableProperty]
    public partial double TodayX { get; set; }

    public ObservableCollection<GanttRowViewModel> Rows { get; } = new();

    public double PixelsPerDay => _pixelsPerDayByZoom[SelectedZoomIndex];

    private IReadOnlyList<WbsNode> _nodes = Array.Empty<WbsNode>();
    private DateTime _projectStart;

    public string[] ZoomOptions { get; } = { "Day", "Week", "Month" };

    public GanttViewModel(
        IProjectStateService projectStateService,
        IWbsService wbsService,
        IDialogService dialogService)
    {
        _projectStateService = projectStateService;
        _wbsService = wbsService;
        _dialogService = dialogService;
        TotalWidth = 1200;
        TotalHeight = 200;
    }

    public void OnNavigatedTo(object? parameter) => _ = LoadAsync();
    public void OnNavigatedFrom() { }

    partial void OnSelectedZoomIndexChanged(int value) => RebuildRows();

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (_projectStateService.ActiveProject is not { Id: var projectId } project) return;
        try
        {
            _nodes = await _wbsService.GetAllByProjectAsync(projectId);

            _projectStart = project.StartDate.HasValue
                ? project.StartDate.Value
                : _nodes.Where(n => n.StartDate.HasValue)
                        .Select(n => n.StartDate!.Value)
                        .DefaultIfEmpty(DateTime.UtcNow.Date)
                        .Min();

            RebuildRows();
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Gantt Error", ex.Message);
        }
    }

    private void RebuildRows()
    {
        Rows.Clear();
        if (_nodes.Count == 0) return;

        double ppd = _pixelsPerDayByZoom[SelectedZoomIndex];
        const double rowHeight = 48;
        double maxRight = 0;

        for (int i = 0; i < _nodes.Count; i++)
        {
            WbsNode node = _nodes[i];
            double rowTop = i * rowHeight;
            bool hasBar = node.StartDate.HasValue && node.DueDate.HasValue;
            double barLeft = 0, barWidth = 0;

            if (hasBar)
            {
                barLeft = (node.StartDate!.Value - _projectStart).TotalDays * ppd;
                barWidth = Math.Max(4, (node.DueDate!.Value - node.StartDate.Value).TotalDays * ppd);
                maxRight = Math.Max(maxRight, barLeft + barWidth);
            }

            Rows.Add(new GanttRowViewModel
            {
                Label = $"{node.Code}  {node.Title}",
                RowTop = rowTop,
                HasBar = hasBar,
                BarLeft = barLeft,
                BarWidth = barWidth,
                Status = node.Status
            });
        }

        TotalHeight = _nodes.Count * rowHeight + 40;
        TotalWidth = Math.Max(800, maxRight + 120);
        TodayX = (DateTime.UtcNow.Date - _projectStart).TotalDays * ppd;
    }
}

