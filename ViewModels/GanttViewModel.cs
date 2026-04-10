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
    private readonly IMilestoneService _milestoneService;
    private readonly IGanttLayoutService _ganttLayoutService;

    /// <summary>Height of the timeline ruler strip at the top of the canvas.</summary>
    public const double HeaderHeight = 40.0;

    /// <summary>Width of the fixed task-label column.</summary>
    public const double LabelWidth = 220.0;

    [ObservableProperty]
    public partial int SelectedZoomIndex { get; set; }

    [ObservableProperty]
    public partial double TotalWidth { get; set; }

    [ObservableProperty]
    public partial double TotalHeight { get; set; }

    [ObservableProperty]
    public partial double TodayX { get; set; }

    private IReadOnlyList<GanttHeaderTick>? _headerTicks;
    public IReadOnlyList<GanttHeaderTick>? HeaderTicks
    {
        get => _headerTicks;
        private set => SetProperty(ref _headerTicks, value);
    }

    private IReadOnlyList<GanttMilestoneMarker>? _milestoneMarkers;
    public IReadOnlyList<GanttMilestoneMarker>? MilestoneMarkers
    {
        get => _milestoneMarkers;
        private set => SetProperty(ref _milestoneMarkers, value);
    }

    private IReadOnlyList<GanttDependencyArrow>? _dependencyArrows;
    public IReadOnlyList<GanttDependencyArrow>? DependencyArrows
    {
        get => _dependencyArrows;
        private set => SetProperty(ref _dependencyArrows, value);
    }

    public ObservableCollection<GanttRow> Rows { get; } = new();

    private IReadOnlyList<WbsNode> _nodes = Array.Empty<WbsNode>();
    private IReadOnlyList<Milestone> _milestones = Array.Empty<Milestone>();
    private IReadOnlyList<NodeDependency> _dependencies = Array.Empty<NodeDependency>();
    private DateTime? _projectStartDate;

    public string[] ZoomOptions { get; } = { "Day", "Week", "Month" };

    public GanttViewModel(
        IProjectStateService projectStateService,
        IWbsService wbsService,
        IDialogService dialogService,
        IMilestoneService milestoneService,
        IGanttLayoutService ganttLayoutService)
    {
        _projectStateService = projectStateService;
        _wbsService = wbsService;
        _dialogService = dialogService;
        _milestoneService = milestoneService;
        _ganttLayoutService = ganttLayoutService;
        TotalWidth = 1200;
        TotalHeight = 200;
    }

    public void OnNavigatedTo(object? parameter) => _ = LoadAsync();
    public void OnNavigatedFrom() { }

    partial void OnSelectedZoomIndexChanged(int value) => RebuildLayout();

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (_projectStateService.ActiveProject is not { Id: var projectId } project) return;
        try
        {
            _nodes        = await _wbsService.GetAllByProjectAsync(projectId);
            _milestones   = await _milestoneService.GetByProjectAsync(projectId);
            _dependencies = await _wbsService.GetAllDependenciesByProjectAsync(projectId);

            _projectStartDate = project.StartDate;

            RebuildLayout();
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Gantt Error", ex.Message);
        }
    }

    private void RebuildLayout()
    {
        Rows.Clear();
        if (_nodes.Count == 0)
        {
            TotalWidth       = 1200;
            TotalHeight      = 200;
            TodayX           = 0;
            HeaderTicks      = null;
            MilestoneMarkers = null;
            DependencyArrows = null;
            return;
        }

        IReadOnlySet<Guid> blockedIds = _wbsService.ResolveBlockedNodeIds(_nodes, _dependencies);
        GanttLayout layout = _ganttLayoutService.Build(
            _nodes, _milestones, _dependencies, blockedIds, _projectStartDate, SelectedZoomIndex);

        foreach (GanttRow row in layout.Rows)
            Rows.Add(row);

        HeaderTicks      = layout.HeaderTicks;
        MilestoneMarkers = layout.MilestoneMarkers;
        DependencyArrows = layout.DependencyArrows;
        TotalWidth       = layout.TotalWidth;
        TotalHeight      = layout.TotalHeight;
        TodayX           = layout.TodayX;
    }
}
