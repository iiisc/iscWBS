using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using iscWBS.Core.Models;
using iscWBS.Core.Services;
using iscWBS.Helpers;

namespace iscWBS.ViewModels;

public sealed partial class WbsTreeViewModel : ObservableObject
{
    private readonly WbsService _wbsService;
    private readonly ProjectStateService _projectStateService;
    private readonly DialogService _dialogService;
    private readonly MilestoneService _milestoneService;

    private bool _isLoadingEditFields;
    private bool _hasPendingEdit;
    private int _saveVersion;
    private Guid _pendingSelectNodeId;
    private CancellationTokenSource? _pageCts;

    public IReadOnlyList<DependencyTypeOption> DependencyTypeOptions { get; } =
        Enum.GetValues<DependencyType>()
            .Select(t => new DependencyTypeOption(
                t,
                $"{DisplayText.GetLabel(t)} ({DependencyConstraints.GetAbbreviation(t)})",
                DisplayText.GetDescription(t)))
            .ToList();

    [ObservableProperty]
    public partial WbsNode? SelectedNode { get; set; }

    [ObservableProperty]
    public partial string EditTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EditDescription { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EditAssignedTo { get; set; } = string.Empty;

    [ObservableProperty]
    public partial WbsStatus EditStatus { get; set; }

    partial void OnEditStatusChanged(WbsStatus value)
    {
        if (_isLoadingEditFields || SelectedNode is not { } node) return;

        IReadOnlyList<NodeDependency> blockers = DependencyConstraints.TransitionBlockers(
            value,
            DiagramDependencies.Where(d => d.SuccessorId == node.Id),
            (DiagramNodes ?? []).ToDictionary(n => n.Id, n => n.Status));

        if (blockers.Count == 0)
        {
            RefreshBlockedIds();
            ScheduleSave();
            return;
        }

        _isLoadingEditFields = true;
        try   { EditStatus = node.Status; }
        finally { _isLoadingEditFields = false; }

        // x:Bind TwoWay suppresses PropertyChanged for EditStatus while the ComboBox SelectionChanged
        // dispatch is still on the stack, so re-notify once it clears for the ComboBox to show the reverted value.
        DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() => OnPropertyChanged(nameof(EditStatus)));

        bool starting = value == WbsStatus.InProgress;
        string details = string.Join("\n", NodeDependencies
            .Where(r => blockers.Any(b => b.Id == r.Dependency.Id))
            .Select(r => $"  • {r.PredecessorCode} {r.PredecessorTitle}: {r.BlockingReason}"));
        _ = _dialogService.ShowErrorAsync(
            Strings.Get(starting ? "Wbs_CannotStartTitle" : "Wbs_CannotCompleteTitle"),
            $"{Strings.Get(starting ? "Wbs_CannotStartIntro" : "Wbs_CannotCompleteIntro")}\n\n{details}");
    }

    partial void OnEditTitleChanged(string value) => ScheduleSave();
    partial void OnEditDescriptionChanged(string value) => ScheduleSave();
    partial void OnEditAssignedToChanged(string value) => ScheduleSave();
    partial void OnEditEstimatedHoursChanged(double value) => ScheduleSave();
    partial void OnEditActualHoursChanged(double value) => ScheduleSave();
    partial void OnEditStartDateChanged(DateTimeOffset? value) => ScheduleSave();
    partial void OnEditDueDateChanged(DateTimeOffset? value) => ScheduleSave();
    partial void OnEditIsDeliverableChanged(bool value) => ScheduleSave();

    [ObservableProperty]
    public partial double EditEstimatedHours { get; set; }

    [ObservableProperty]
    public partial double EditActualHours { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset? EditStartDate { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset? EditDueDate { get; set; }

    [ObservableProperty]
    public partial bool EditIsDeliverable { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<WbsNode>? DiagramNodes { get; set; }

    [ObservableProperty]
    public partial IReadOnlySet<Guid>? BlockedNodeIds { get; private set; }

    [ObservableProperty]
    public partial IReadOnlySet<Guid>? CompletionBlockedNodeIds { get; private set; }

    [ObservableProperty]
    public partial IReadOnlySet<Guid>? ViolatedCompleteNodeIds { get; private set; }

    [ObservableProperty]
    public partial IReadOnlySet<Guid>? ViolatedInProgressNodeIds { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<NodeDependency> DiagramDependencies { get; private set; } = [];

    /// <summary>Predecessor rows of <see cref="SelectedNode"/>.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<DependencyRowViewModel> NodeDependencies { get; private set; } = [];

    /// <summary>Every node but <see cref="SelectedNode"/>, for the add-predecessor picker.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<WbsNode> PredecessorOptions { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<MilestoneRowViewModel> LinkedMilestones { get; private set; } = [];

    [ObservableProperty]
    public partial bool IsSaving { get; set; }

    public bool HasNoNodes => DiagramNodes is null || DiagramNodes.Count == 0;

    partial void OnDiagramNodesChanged(IReadOnlyList<WbsNode>? value)
        => OnPropertyChanged(nameof(HasNoNodes));

    [ObservableProperty]
    public partial WbsNode? NewDepPredecessor { get; set; }

    [ObservableProperty]
    public partial DependencyTypeOption? NewDepTypeOption { get; set; }

    public bool IsDetailVisible => SelectedNode is not null;

    /// <summary>The id of <see cref="SelectedNode"/> for the diagram highlight, or <see cref="Guid.Empty"/> when nothing is selected.</summary>
    public Guid SelectedNodeId => SelectedNode?.Id ?? Guid.Empty;

    public IReadOnlyList<WbsStatus> StatusOptions { get; } =
        Enum.GetValues<WbsStatus>().Except([WbsStatus.Blocked]).ToList();

    public WbsTreeViewModel(
        WbsService wbsService,
        ProjectStateService projectStateService,
        DialogService dialogService,
        MilestoneService milestoneService)
    {
        _wbsService = wbsService;
        _projectStateService = projectStateService;
        _dialogService = dialogService;
        _milestoneService = milestoneService;
        NewDepTypeOption = DependencyTypeOptions[0];
    }

    public void OnNavigatedTo(object? parameter)
    {
        _pageCts?.Cancel();
        _pageCts?.Dispose();
        _pageCts = new CancellationTokenSource();
        if (parameter is Guid nodeId)
            _pendingSelectNodeId = nodeId;
        _ = LoadDiagramNodesAsync(PageToken);
    }

    public void OnNavigatedFrom()
    {
        FlushPendingEdit();
        _pageCts?.Cancel();
        _pageCts?.Dispose();
        _pageCts = null;
    }

    partial void OnSelectedNodeChanged(WbsNode? value)
    {
        _saveVersion++;
        _hasPendingEdit = false;
        IsSaving = false;
        OnPropertyChanged(nameof(IsDetailVisible));
        OnPropertyChanged(nameof(SelectedNodeId));
        NewDepPredecessor = null;
        RefreshSelectionLists();
        LinkedMilestones = [];
        if (value is null) return;
        LoadEditFieldsFromNode(value);
        _ = LoadLinkedMilestonesAsync(value.Id);
    }

    private CancellationToken PageToken => _pageCts?.Token ?? default;

    [RelayCommand]
    private async Task AddRootNodeAsync()
    {
        if (_projectStateService.ActiveProject is { Id: var projectId })
            await AddNodeAsync(title => _wbsService.AddRootNodeAsync(projectId, title), "ErrorTitle_AddNode");
    }

    [RelayCommand]
    private async Task AddChildNodeAsync(WbsNode? node)
    {
        if (node is not null)
            await AddNodeAsync(title => _wbsService.AddChildNodeAsync(node.Id, title), "ErrorTitle_AddChild");
    }

    [RelayCommand]
    private async Task AddSiblingNodeAsync(WbsNode? node)
    {
        if (node is not null)
            await AddNodeAsync(title => _wbsService.AddSiblingNodeAsync(node.Id, title), "ErrorTitle_AddSibling");
    }

    private async Task AddNodeAsync(Func<string, Task<WbsNode>> add, string errorTitleKey)
    {
        try
        {
            await add(Strings.Get("Wbs_NewNodeTitle"));
            await LoadDiagramNodesAsync(PageToken);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync(errorTitleKey, ex);
        }
    }

    [RelayCommand]
    private async Task DeleteNodeAsync(WbsNode? node)
    {
        if (node is null) return;

        bool confirmed = await _dialogService.ShowConfirmAsync(
            Strings.Get("Wbs_DeleteNodeTitle"),
            Strings.Format("Wbs_DeleteNodeConfirm", node.Title));
        if (!confirmed) return;

        try
        {
            await _wbsService.DeleteNodeAsync(node.Id);
            if (SelectedNode == node)
                SelectedNode = null;
            await LoadDiagramNodesAsync(PageToken);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("ErrorTitle_Delete", ex);
        }
    }

    [RelayCommand]
    private void ClearSelection()
    {
        FlushPendingEdit();
        SelectedNode = null;
    }

    /// <summary>Saves the selected node, then reloads and warns about any dependencies the edit newly broke.</summary>
    private async Task SaveNodeAsync()
    {
        if (SelectedNode is not { } node) return;

        // Snapshot before the edit is applied: the selected node is usually the same instance as in DiagramNodes.
        IReadOnlyList<WbsNode> nodes = DiagramNodes ?? [];
        IReadOnlySet<Guid> completeBefore   = DependencyConstraints.ViolatedCompleteNodeIds(nodes, DiagramDependencies);
        IReadOnlySet<Guid> inProgressBefore = DependencyConstraints.ViolatedInProgressNodeIds(nodes, DiagramDependencies);

        node.Title          = EditTitle;
        node.Description    = EditDescription;
        node.AssignedTo     = EditAssignedTo;
        node.Status         = EditStatus;
        node.EstimatedHours = EditEstimatedHours;
        node.ActualHours    = EditActualHours;
        node.StartDate      = EditStartDate?.Date;
        node.DueDate        = EditDueDate?.Date;
        node.IsDeliverable  = EditIsDeliverable;

        try
        {
            await _wbsService.UpdateNodeAsync(node);
            await LoadDiagramNodesAsync(PageToken);

            await WarnNewlyViolatedAsync(ViolatedCompleteNodeIds, completeBefore,
                "Wbs_CompleteViolatedTitle", "Wbs_CompleteViolatedIntro", "Wbs_CompleteViolatedOutro");

            await WarnNewlyViolatedAsync(ViolatedInProgressNodeIds, inProgressBefore,
                "Wbs_InProgressViolatedTitle", "Wbs_InProgressViolatedIntro", "Wbs_InProgressViolatedOutro");
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("ErrorTitle_Save", ex);
        }
        finally
        {
            IsSaving = false;
        }
    }

    private async Task WarnNewlyViolatedAsync(
        IReadOnlySet<Guid>? current, IReadOnlySet<Guid> before, string titleKey, string introKey, string outroKey)
    {
        if (current is not { Count: > 0 } || DiagramNodes is null) return;

        List<string> newlyViolated = DiagramNodes
            .Where(n => current.Contains(n.Id) && !before.Contains(n.Id))
            .Select(n => $"  • {n.Code} {n.Title}")
            .ToList();

        if (newlyViolated.Count > 0)
            await _dialogService.ShowErrorAsync(
                Strings.Get(titleKey),
                $"{Strings.Get(introKey)}\n\n{string.Join("\n", newlyViolated)}\n\n{Strings.Get(outroKey)}");
    }

    [RelayCommand]
    private async Task AddDependencyAsync()
    {
        if (SelectedNode is null || NewDepPredecessor is null) return;

        var dep = new NodeDependency
        {
            PredecessorId = NewDepPredecessor.Id,
            SuccessorId = SelectedNode.Id,
            Type = NewDepTypeOption?.Type ?? DependencyType.FinishToStart
        };
        try
        {
            await _wbsService.AddDependencyAsync(dep);
            NewDepPredecessor = null;
            await LoadDiagramNodesAsync(PageToken);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("ErrorTitle_AddDependency", ex);
        }
    }

    [RelayCommand]
    private async Task RemoveDependencyAsync(DependencyRowViewModel? row)
    {
        if (row is null) return;

        bool confirmed = await _dialogService.ShowConfirmAsync(
            Strings.Get("Wbs_RemoveDependencyTitle"),
            Strings.Format("Wbs_RemoveDependencyConfirm", $"{row.PredecessorCode} {row.PredecessorTitle}"));
        if (!confirmed) return;

        try
        {
            await _wbsService.RemoveDependencyAsync(row.Dependency.Id);
            await LoadDiagramNodesAsync(PageToken);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("ErrorTitle_RemoveDependency", ex);
        }
    }

    private async Task LoadDiagramNodesAsync(CancellationToken ct = default)
    {
        if (_projectStateService.ActiveProject is not { Id: var projectId }) return;
        try
        {
            ct.ThrowIfCancellationRequested();
            IReadOnlyList<WbsNode> nodes = await _wbsService.GetAllByProjectAsync(projectId);
            ct.ThrowIfCancellationRequested();
            IReadOnlyList<NodeDependency> deps = await _wbsService.GetAllDependenciesByProjectAsync(projectId);
            RecomputeConstraintSets(nodes, deps);
            DiagramDependencies = deps;
            DiagramNodes        = nodes;
            RefreshSelectionLists();

            if (_pendingSelectNodeId != Guid.Empty)
            {
                WbsNode? target = nodes.FirstOrDefault(n => n.Id == _pendingSelectNodeId);
                _pendingSelectNodeId = Guid.Empty;
                if (target is not null)
                    SelectDiagramNode(target);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("ErrorTitle_Load", ex);
        }
    }

    /// <summary>Rebuilds the selected node's predecessor rows and picker from the loaded project, so they never lag the diagram.</summary>
    private void RefreshSelectionLists()
    {
        if (SelectedNode is not { } selected || DiagramNodes is null)
        {
            NodeDependencies = [];
            PredecessorOptions = [];
            return;
        }

        Dictionary<Guid, WbsNode> nodesById = DiagramNodes.ToDictionary(n => n.Id);
        NodeDependencies = DiagramDependencies
            .Where(d => d.SuccessorId == selected.Id && nodesById.ContainsKey(d.PredecessorId))
            .Select(d =>
            {
                WbsNode pred = nodesById[d.PredecessorId];
                return new DependencyRowViewModel
                {
                    Dependency       = d,
                    PredecessorCode  = pred.Code,
                    PredecessorTitle = pred.Title,
                    TypeLabel        = DependencyConstraints.GetAbbreviation(d.Type),
                    TypeDescription  = DisplayText.GetDescription(d.Type),
                    IsBlocking       = DependencyConstraints.IsViolated(d.Type, pred.Status),
                    BlockingReason   = DisplayText.GetBlockingReason(d.Type, pred.Status),
                };
            })
            .ToList();

        // Replacing the picker's items clears its selection, so carry a half-made choice across reloads.
        Guid? pickedId = NewDepPredecessor?.Id;
        PredecessorOptions = DiagramNodes.Where(n => n.Id != selected.Id).ToList();
        NewDepPredecessor = PredecessorOptions.FirstOrDefault(n => n.Id == pickedId);
    }

    /// <summary>Recomputes the constraint sets with the pending status edit applied, ahead of the debounced save.</summary>
    private void RefreshBlockedIds()
    {
        if (DiagramNodes is null || SelectedNode is null) return;

        WbsNode? target = DiagramNodes.FirstOrDefault(n => n.Id == SelectedNode.Id);
        if (target is null) return;

        WbsStatus original = target.Status;
        target.Status = EditStatus;
        try
        {
            RecomputeConstraintSets(DiagramNodes, DiagramDependencies);
        }
        finally
        {
            target.Status = original;
        }
    }

    // Each new set redraws the whole diagram, so keep the old instance when the contents match.
    private void RecomputeConstraintSets(IReadOnlyList<WbsNode> nodes, IReadOnlyList<NodeDependency> deps)
    {
        BlockedNodeIds            = Unchanged(BlockedNodeIds,            DependencyConstraints.BlockedNodeIds(nodes, deps));
        CompletionBlockedNodeIds  = Unchanged(CompletionBlockedNodeIds,  DependencyConstraints.CompletionBlockedNodeIds(nodes, deps));
        ViolatedCompleteNodeIds   = Unchanged(ViolatedCompleteNodeIds,   DependencyConstraints.ViolatedCompleteNodeIds(nodes, deps));
        ViolatedInProgressNodeIds = Unchanged(ViolatedInProgressNodeIds, DependencyConstraints.ViolatedInProgressNodeIds(nodes, deps));
    }

    private static IReadOnlySet<Guid> Unchanged(IReadOnlySet<Guid>? current, IReadOnlySet<Guid> next)
        => current is not null && current.SetEquals(next) ? current : next;

    /// <summary>Selects a node from the diagram view, populating the detail panel.</summary>
    public void SelectDiagramNode(WbsNode node)
    {
        FlushPendingEdit();
        SelectedNode = node;
    }

    private void LoadEditFieldsFromNode(WbsNode node)
    {
        _isLoadingEditFields = true;
        try
        {
            EditTitle = node.Title;
            EditDescription = node.Description;
            EditAssignedTo = node.AssignedTo;
            EditStatus = node.Status;
            EditEstimatedHours = node.EstimatedHours;
            EditActualHours = node.ActualHours;
            EditStartDate = CalendarDates.ToPickerValue(node.StartDate);
            EditDueDate   = CalendarDates.ToPickerValue(node.DueDate);
            EditIsDeliverable = node.IsDeliverable;
        }
        finally
        {
            _isLoadingEditFields = false;
        }
    }

    private void ScheduleSave()
    {
        if (_isLoadingEditFields || SelectedNode is null) return;
        _hasPendingEdit = true;
        IsSaving = true;
        int version = ++_saveVersion;
        _ = DebouncedSaveAsync(version);
    }

    private async Task DebouncedSaveAsync(int version)
    {
        await Task.Delay(600);
        if (_saveVersion != version) return;
        _hasPendingEdit = false;
        await SaveNodeAsync();
    }

    /// <summary>
    /// Saves an edit still waiting out its debounce, before the selection or page changes under it.
    /// A save already past the debounce is in flight and reloads on its own.
    /// </summary>
    private void FlushPendingEdit()
    {
        _saveVersion++;
        if (!_hasPendingEdit) return;
        _hasPendingEdit = false;
        _ = SaveNodeAsync();
    }

    private async Task LoadLinkedMilestonesAsync(Guid nodeId)
    {
        try
        {
            IReadOnlyList<Milestone> milestones = await _milestoneService.GetByNodeAsync(nodeId);
            if (SelectedNode?.Id != nodeId) return;

            LinkedMilestones = milestones.Select(m => new MilestoneRowViewModel { Milestone = m }).ToList();
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("ErrorTitle_Load", ex);
        }
    }
}
