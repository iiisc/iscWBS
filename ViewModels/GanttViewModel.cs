using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iscWBS.Core.Models;
using iscWBS.Core.Services;
using iscWBS.Helpers;

namespace iscWBS.ViewModels;

public sealed partial class GanttViewModel : ObservableObject
{
    private readonly ProjectStateService _projectStateService;
    private readonly WbsService _wbsService;
    private readonly DialogService _dialogService;
    private readonly MilestoneService _milestoneService;

    [ObservableProperty]
    public partial int SelectedZoomIndex { get; set; }

    [ObservableProperty]
    public partial bool ShowCriticalPath { get; set; }

    public bool HasBaseline => _nodes.Any(n => n.BaselineDue.HasValue);

    /// <summary>Shown after a change moved other work later to keep its dependencies.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRescheduleNotice))]
    public partial string? RescheduleNotice { get; set; }

    public bool HasRescheduleNotice
    {
        get => RescheduleNotice is not null;
        set { if (!value) RescheduleNotice = null; }
    }

    private static readonly GanttLayout EmptyLayout = new([], [], [], [], 1200, 144, 0);

    /// <summary>Replaced as a whole so the view redraws once per rebuild.</summary>
    [ObservableProperty]
    public partial GanttLayout Layout { get; private set; } = EmptyLayout;

    /// <summary>Nothing has dates to draw. Not before the first load, so the empty state doesn't flash up.</summary>
    public bool IsEmpty => _loaded && _milestones.Count == 0 && !_nodes.Any(n => n.StartDate.HasValue && n.DueDate.HasValue);

    private IReadOnlyList<WbsNode> _nodes = Array.Empty<WbsNode>();
    private IReadOnlyList<Milestone> _milestones = Array.Empty<Milestone>();
    private IReadOnlyList<NodeDependency> _dependencies = Array.Empty<NodeDependency>();
    private DateTime? _projectStartDate;
    private bool _loaded;

    public GanttViewModel(
        ProjectStateService projectStateService,
        WbsService wbsService,
        DialogService dialogService,
        MilestoneService milestoneService)
    {
        _projectStateService = projectStateService;
        _wbsService = wbsService;
        _dialogService = dialogService;
        _milestoneService = milestoneService;
    }

    public void OnNavigatedTo() => _ = LoadAsync();

    partial void OnSelectedZoomIndexChanged(int value) => RebuildLayout();

    private async Task LoadAsync()
    {
        if (_projectStateService.ActiveProject is not { Id: var projectId } project) return;
        try
        {
            _nodes        = await _wbsService.GetAllByProjectAsync(projectId);
            _milestones   = await _milestoneService.GetByProjectAsync(projectId);
            _dependencies = await _wbsService.GetAllDependenciesByProjectAsync(projectId);

            _projectStartDate = project.StartDate;
            _loaded = true;
            OnPropertyChanged(nameof(HasBaseline));

            RebuildLayout();
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("ErrorTitle_Gantt", ex);
        }
    }

    private void RebuildLayout()
    {
        OnPropertyChanged(nameof(IsEmpty));
        IReadOnlySet<Guid> blockedIds = DependencyConstraints.BlockedNodeIds(_nodes, _dependencies);
        Layout = GanttLayoutBuilder.Build(
            _nodes, _milestones, _dependencies, blockedIds, _projectStartDate, SelectedZoomIndex);
    }

    /// <summary>Shifts a task dragged on the chart: <paramref name="startDays"/> moves its start, <paramref name="dueDays"/> its due date.</summary>
    public async Task ShiftDatesAsync(Guid nodeId, int startDays, int dueDays)
    {
        if (_nodes.FirstOrDefault(n => n.Id == nodeId) is not { StartDate: { } start, DueDate: { } due } node) return;
        node.StartDate = start.AddDays(startDays);
        // A due date dragged before the start stops at the start.
        node.DueDate = due.AddDays(dueDays) < node.StartDate ? node.StartDate : due.AddDays(dueDays);
        try
        {
            IReadOnlyList<WbsNode> moved = await _wbsService.UpdateNodeAsync(node);
            if (moved.Count > 0)
                RescheduleNotice = string.Join(", ", moved.Select(n => $"{n.Code} {n.Title}"));
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("ErrorTitle_Save", ex);
        }
        await LoadAsync();
    }

    /// <summary>Saves today's dates as the plan to measure later changes against.</summary>
    [RelayCommand]
    private async Task SetBaselineAsync()
    {
        if (_projectStateService.ActiveProject is not { Id: var projectId }) return;
        if (HasBaseline && !await _dialogService.ShowConfirmAsync(
                Strings.Get("Gantt_ReplaceBaselineTitle"), Strings.Get("Gantt_ReplaceBaselineConfirm")))
            return;
        try
        {
            await _wbsService.SetBaselineAsync(projectId);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("ErrorTitle_Save", ex);
        }
        await LoadAsync();
    }
}
