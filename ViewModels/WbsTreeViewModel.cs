using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using iscWBS.Core.Models;
using iscWBS.Core.Services;

namespace iscWBS.ViewModels;

public sealed partial class WbsTreeViewModel : ObservableObject, INavigationAware
{
    private readonly IWbsService _wbsService;
    private readonly IProjectStateService _projectStateService;
    private readonly IDialogService _dialogService;
    private readonly IMilestoneService _milestoneService;

    private bool _isLoadingEditFields;
    private bool _hasPendingEdit;
    private int _saveVersion;
    private CancellationTokenSource? _pageCts;
    private CancellationTokenSource? _selectionCts;
    private IReadOnlyList<NodeDependency> _cachedDependencies = Array.Empty<NodeDependency>();

    public ObservableCollection<DependencyRowViewModel> NodeDependencies { get; } = new();
    public ObservableCollection<WbsNode> AllProjectNodes { get; } = new();
    public ObservableCollection<MilestoneRowViewModel> LinkedMilestones { get; } = new();

    public IReadOnlyList<DependencyTypeOption> DependencyTypeOptions { get; } =
    [
        new(DependencyType.FinishToStart,  "Finish → Start (FS)",  "Predecessor must finish before this node can start"),
        new(DependencyType.StartToStart,   "Start → Start (SS)",   "Predecessor must start before this node can start"),
        new(DependencyType.FinishToFinish, "Finish → Finish (FF)", "Predecessor must finish before this node can finish"),
        new(DependencyType.StartToFinish,  "Start → Finish (SF)",  "Predecessor must start before this node can finish"),
    ];

    [ObservableProperty]
    public partial WbsNodeViewModel? SelectedNode { get; set; }

    [ObservableProperty]
    public partial string EditTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EditDescription { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EditAssignedTo { get; set; } = string.Empty;

    [ObservableProperty]
    public partial WbsStatus? EditStatus { get; set; } = WbsStatus.NotStarted;

    /// <summary>Zero-based index of <see cref="EditStatus"/> within <see cref="StatusOptions"/>, or -1 when null.</summary>
    public int EditStatusIndex
    {
        get => EditStatus.HasValue ? (int)EditStatus.Value : -1;
        set => EditStatus = value >= 0 ? (WbsStatus)value : (WbsStatus?)null;
    }

    /// <summary>
    /// Non-nullable binding surface for <see cref="ComboBox.SelectedItem"/>.
    /// Avoids boxing <see cref="WbsStatus?"/> as a nullable so the ComboBox
    /// equality scan against <see cref="StatusOptions"/> always succeeds.
    /// </summary>
    public WbsStatus EditStatusItem
    {
        get => EditStatus ?? WbsStatus.NotStarted;
        set => EditStatus = value;
    }

    partial void OnEditStatusChanged(WbsStatus? value)
    {
        OnPropertyChanged(nameof(EditStatusIndex));
        OnPropertyChanged(nameof(EditStatusItem));
        if (!_isLoadingEditFields) RefreshBlockedIds();

        if (!_isLoadingEditFields && value is WbsStatus.InProgress or WbsStatus.Complete
            && SelectedNode is { } selectedNode)
        {
            IReadOnlyList<NodeDependency> nodePredecessors = _cachedDependencies
                .Where(d => d.SuccessorId == selectedNode.Node.Id)
                .ToList();

            IReadOnlyDictionary<Guid, WbsStatus> statusMap =
                DiagramNodes?.ToDictionary(n => n.Id, n => n.Status)
                ?? (IReadOnlyDictionary<Guid, WbsStatus>)new Dictionary<Guid, WbsStatus>();

            IReadOnlyList<NodeDependency> blockers = _wbsService.GetStatusTransitionBlockers(
                value.Value, nodePredecessors, statusMap);

            if (blockers.Count > 0)
            {
                // Revert the ViewModel state synchronously so it is never wrong.
                _isLoadingEditFields = true;
                try   { EditStatus = selectedNode.Node.Status; }
                finally { _isLoadingEditFields = false; }
                RefreshBlockedIds();

                // x:Bind TwoWay suppresses PropertyChanged for EditStatusIndex while
                // the ComboBox SelectionChanged dispatch is still on the stack.
                // Post a deferred notification so the ComboBox re-reads the already-
                // reverted value once that dispatch — and its feedback-loop guard — clears.
                DispatcherQueue.GetForCurrentThread()?.TryEnqueue(
                    () => OnPropertyChanged(nameof(EditStatusItem)));

                string title = value == WbsStatus.InProgress
                    ? "Cannot Mark as In Progress"
                    : "Cannot Mark as Complete";
                string intro = value == WbsStatus.InProgress
                    ? "This node cannot be started until the following predecessor constraints are met:"
                    : "This node cannot be completed until the following predecessor constraints are met:";

                List<DependencyRowViewModel> violators = blockers
                    .Select(b => NodeDependencies.FirstOrDefault(r => r.Dependency.Id == b.Id))
                    .OfType<DependencyRowViewModel>()
                    .ToList();

                _ = NotifyStatusBlockedAsync(title, intro, violators);
                return;
            }
        }

        ScheduleSave();
    }

    partial void OnEditTitleChanged(string value) => ScheduleSave();
    partial void OnEditDescriptionChanged(string value) => ScheduleSave();
    partial void OnEditAssignedToChanged(string value) => ScheduleSave();
    partial void OnEditEstimatedHoursChanged(double value) => ScheduleSave();
    partial void OnEditActualHoursChanged(double value) => ScheduleSave();
    partial void OnEditStartDateChanged(DateTimeOffset? value) => ScheduleSave();
    partial void OnEditDueDateChanged(DateTimeOffset? value) => ScheduleSave();

    [ObservableProperty]
    public partial double EditEstimatedHours { get; set; }

    [ObservableProperty]
    public partial double EditActualHours { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset? EditStartDate { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset? EditDueDate { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<WbsNode>? DiagramNodes { get; set; }

    private IReadOnlySet<Guid>? _blockedNodeIds;
    public IReadOnlySet<Guid>? BlockedNodeIds
    {
        get => _blockedNodeIds;
        private set => SetProperty(ref _blockedNodeIds, value);
    }

    private IReadOnlySet<Guid>? _completionBlockedNodeIds;
    public IReadOnlySet<Guid>? CompletionBlockedNodeIds
    {
        get => _completionBlockedNodeIds;
        private set => SetProperty(ref _completionBlockedNodeIds, value);
    }

    private IReadOnlySet<Guid>? _violatedCompleteNodeIds;
    public IReadOnlySet<Guid>? ViolatedCompleteNodeIds
    {
        get => _violatedCompleteNodeIds;
        private set => SetProperty(ref _violatedCompleteNodeIds, value);
    }

    private IReadOnlySet<Guid>? _violatedInProgressNodeIds;
    public IReadOnlySet<Guid>? ViolatedInProgressNodeIds
    {
        get => _violatedInProgressNodeIds;
        private set => SetProperty(ref _violatedInProgressNodeIds, value);
    }

    private IReadOnlyList<NodeDependency>? _diagramDependencies;
    public IReadOnlyList<NodeDependency>? DiagramDependencies
    {
        get => _diagramDependencies;
        private set => SetProperty(ref _diagramDependencies, value);
    }

    [ObservableProperty]
    public partial bool IsSaving { get; set; }

    public bool HasNoNodes => DiagramNodes is null || DiagramNodes.Count == 0;

    partial void OnDiagramNodesChanged(IReadOnlyList<WbsNode>? value)
        => OnPropertyChanged(nameof(HasNoNodes));

    [ObservableProperty]
    public partial WbsNode? NewDepPredecessor { get; set; }

    [ObservableProperty]
    public partial DependencyTypeOption? NewDepTypeOption { get; set; } =
        new(DependencyType.FinishToStart, "Finish → Start (FS)", "Predecessor must finish before this node can start");

    public bool IsDetailVisible => SelectedNode is not null;

    /// <summary>The id of <see cref="SelectedNode"/> for the diagram highlight, or <see cref="Guid.Empty"/> when nothing is selected.</summary>
    public Guid SelectedNodeId => SelectedNode?.Node.Id ?? Guid.Empty;

    public IReadOnlyList<WbsStatus> StatusOptions { get; } =
        Enum.GetValues<WbsStatus>().Except([WbsStatus.Blocked]).ToList();

    public WbsTreeViewModel(
        IWbsService wbsService,
        IProjectStateService projectStateService,
        IDialogService dialogService,
        IMilestoneService milestoneService)
    {
        _wbsService = wbsService;
        _projectStateService = projectStateService;
        _dialogService = dialogService;
        _milestoneService = milestoneService;
    }

    public void OnNavigatedTo(object? parameter)
    {
        _pageCts?.Cancel();
        _pageCts?.Dispose();
        _pageCts = new CancellationTokenSource();
        _ = LoadRootNodesAsync();
    }

    public void OnNavigatedFrom()
    {
        if (_hasPendingEdit && SelectedNode is { } nodeVm)
        {
            _hasPendingEdit = false;
            ApplyEditFieldsToNode(nodeVm);
            _ = _wbsService.UpdateNodeAsync(nodeVm.Node); // best-effort, no error UI
        }
        _saveVersion++;
        _pageCts?.Cancel();
        _pageCts?.Dispose();
        _pageCts = null;
        _selectionCts?.Cancel();
        _selectionCts?.Dispose();
        _selectionCts = null;
    }

    partial void OnSelectedNodeChanged(WbsNodeViewModel? value)
    {
        _saveVersion++;
        IsSaving = false;
        _selectionCts?.Cancel();
        _selectionCts?.Dispose();
        _selectionCts = null;
        OnPropertyChanged(nameof(IsDetailVisible));
        OnPropertyChanged(nameof(SelectedNodeId));
        if (value is null)
        {
            NodeDependencies.Clear();
            AllProjectNodes.Clear();
            LinkedMilestones.Clear();
            return;
        }
        _selectionCts = new CancellationTokenSource();
        LoadEditFieldsFromNode(value.Node);
        _ = LoadDependenciesAsync(value.Node.Id, _selectionCts.Token);
        _ = LoadAllProjectNodesAsync(_selectionCts.Token);
        _ = LoadLinkedMilestonesAsync(value.Node.Id, _selectionCts.Token);
    }

    private Task LoadRootNodesAsync()
        => LoadDiagramNodesAsync(_pageCts?.Token ?? default);

    [RelayCommand]
    private async Task AddRootNodeAsync()
    {
        if (_projectStateService.ActiveProject is not { Id: var projectId }) return;
        try
        {
            await _wbsService.AddRootNodeAsync(projectId, "New Node");
            await LoadRootNodesAsync();
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Add Node Error", ex.Message);
        }
    }

    [RelayCommand]
    private async Task AddChildNodeAsync(WbsNodeViewModel? nodeVm)
    {
        if (nodeVm is null) return;
        try
        {
            await _wbsService.AddChildNodeAsync(nodeVm.Node.Id, "New Node");
            await LoadDiagramNodesAsync(_pageCts?.Token ?? default);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Add Child Error", ex.Message);
        }
    }

    [RelayCommand]
    private async Task AddSiblingNodeAsync(WbsNodeViewModel? nodeVm)
    {
        if (nodeVm is null) return;
        try
        {
            await _wbsService.AddSiblingNodeAsync(nodeVm.Node.Id, "New Node");
            await LoadRootNodesAsync();
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Add Sibling Error", ex.Message);
        }
    }

    [RelayCommand]
    private async Task DeleteNodeAsync(WbsNodeViewModel? nodeVm)
    {
        if (nodeVm is null) return;

        bool confirmed = await _dialogService.ShowConfirmAsync(
            "Delete Node",
            $"Delete \"{nodeVm.Node.Title}\" and all its children?");
        if (!confirmed) return;

        try
        {
            await _wbsService.DeleteNodeAsync(nodeVm.Node.Id);
            if (SelectedNode == nodeVm)
            {
                _hasPendingEdit = false;
                _saveVersion++;
                SelectedNode = null;
            }
            await LoadRootNodesAsync();
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Delete Error", ex.Message);
        }
    }

    [RelayCommand]
    private void ClearSelection()
    {
        if (_hasPendingEdit && SelectedNode is { } nodeVm)
        {
            _hasPendingEdit = false;
            _saveVersion++;
            ApplyEditFieldsToNode(nodeVm);
            _ = _wbsService.UpdateNodeAsync(nodeVm.Node);
        }
        else
        {
            _saveVersion++;
        }
        IsSaving = false;
        SelectedNode = null;
    }

    private void ApplyEditFieldsToNode(WbsNodeViewModel nodeVm)
    {
        nodeVm.Node.Title       = EditTitle;
        nodeVm.Node.Description = EditDescription;
        nodeVm.Node.AssignedTo  = EditAssignedTo;
        nodeVm.Node.Status      = EditStatus ?? WbsStatus.NotStarted;
        nodeVm.Node.EstimatedHours = EditEstimatedHours;
        nodeVm.Node.ActualHours    = EditActualHours;
        nodeVm.Node.StartDate = EditStartDate?.UtcDateTime;
        nodeVm.Node.DueDate   = EditDueDate?.UtcDateTime;
        nodeVm.Title = EditTitle;
    }

    private async Task FlushSaveAsync(
        WbsNodeViewModel nodeVm,
        CancellationToken ct,
        IReadOnlySet<Guid>? preSaveCompleteViolations = null,
        IReadOnlySet<Guid>? preSaveInProgressViolations = null)
    {
        try
        {
            // Callers that mutate a node before calling us must pass the snapshot taken
            // BEFORE the mutation; otherwise we fall back to computing it here.
            preSaveCompleteViolations    ??= DiagramNodes is not null
                ? _wbsService.ResolveViolatedCompleteNodeIds(DiagramNodes, _cachedDependencies)
                : (IReadOnlySet<Guid>)new HashSet<Guid>();
            preSaveInProgressViolations ??= DiagramNodes is not null
                ? _wbsService.ResolveViolatedInProgressNodeIds(DiagramNodes, _cachedDependencies)
                : (IReadOnlySet<Guid>)new HashSet<Guid>();

            await _wbsService.UpdateNodeAsync(nodeVm.Node);
            await LoadDiagramNodesAsync(ct);

            // Warn about any Complete nodes whose constraints were just invalidated.
            if (ViolatedCompleteNodeIds is { Count: > 0 } && DiagramNodes is not null)
            {
                List<string> newlyViolated = DiagramNodes
                    .Where(n => ViolatedCompleteNodeIds.Contains(n.Id) &&
                                !preSaveCompleteViolations.Contains(n.Id))
                    .Select(n => $"  \u2022 {n.Code} {n.Title}")
                    .ToList();

                if (newlyViolated.Count > 0)
                    await _dialogService.ShowErrorAsync(
                        "Completed Node Affected",
                        $"This change has created dependency violations for the following completed nodes:\n\n{string.Join("\n", newlyViolated)}\n\nThese nodes remain marked as Complete but their predecessor constraints are no longer satisfied. Review their status if needed.");
            }

            // Warn about any In Progress nodes whose start constraints were just invalidated.
            if (ViolatedInProgressNodeIds is { Count: > 0 } && DiagramNodes is not null)
            {
                List<string> newlyViolated = DiagramNodes
                    .Where(n => ViolatedInProgressNodeIds.Contains(n.Id) &&
                                !preSaveInProgressViolations.Contains(n.Id))
                    .Select(n => $"  \u2022 {n.Code} {n.Title}")
                    .ToList();

                if (newlyViolated.Count > 0)
                    await _dialogService.ShowErrorAsync(
                        "In Progress Node Affected",
                        $"This change has created start constraint violations for the following in-progress nodes:\n\n{string.Join("\n", newlyViolated)}\n\nThese nodes are already in progress but their start dependencies are no longer satisfied. Review their status if needed.");
            }
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Save Error", ex.Message);
        }
        finally
        {
            IsSaving = false;
        }
    }

    private async Task SaveNodeAsync()
    {
        if (SelectedNode is null) return;
        IReadOnlySet<Guid> preSaveComplete    = DiagramNodes is not null
            ? _wbsService.ResolveViolatedCompleteNodeIds(DiagramNodes, _cachedDependencies)
            : (IReadOnlySet<Guid>)new HashSet<Guid>();
        IReadOnlySet<Guid> preSaveInProgress  = DiagramNodes is not null
            ? _wbsService.ResolveViolatedInProgressNodeIds(DiagramNodes, _cachedDependencies)
            : (IReadOnlySet<Guid>)new HashSet<Guid>();
        ApplyEditFieldsToNode(SelectedNode);
        await FlushSaveAsync(SelectedNode, _pageCts?.Token ?? default, preSaveComplete, preSaveInProgress);
    }

    [RelayCommand]
    private async Task AddDependencyAsync()
    {
        if (SelectedNode is null || NewDepPredecessor is null) return;
        if (NewDepPredecessor.Id == SelectedNode.Node.Id) return;
        if (NodeDependencies.Any(d => d.Dependency.PredecessorId == NewDepPredecessor.Id)) return;

        var dep = new NodeDependency
        {
            PredecessorId = NewDepPredecessor.Id,
            SuccessorId = SelectedNode.Node.Id,
            Type = NewDepTypeOption?.Type ?? DependencyType.FinishToStart
        };
        try
        {
            await _wbsService.AddDependencyAsync(dep);
            await LoadDependenciesAsync(SelectedNode.Node.Id);
            NewDepPredecessor = null;
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Add Dependency Error", ex.Message);
        }
    }

    [RelayCommand]
    private async Task RemoveDependencyAsync(DependencyRowViewModel? row)
    {
        if (row is null || SelectedNode is null) return;

        bool confirmed = await _dialogService.ShowConfirmAsync(
            "Remove Dependency",
            $"Remove dependency from \"{row.PredecessorCode} {row.PredecessorTitle}\"?");
        if (!confirmed) return;

        try
        {
            await _wbsService.RemoveDependencyAsync(row.Dependency.Id);
            await LoadDependenciesAsync(SelectedNode.Node.Id);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Remove Dependency Error", ex.Message);
        }
    }

    private async Task LoadDependenciesAsync(Guid nodeId, CancellationToken ct = default)
    {
        NodeDependencies.Clear();
        NewDepPredecessor = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            IReadOnlyList<NodeDependency> deps = await _wbsService.GetDependenciesAsync(nodeId);
            foreach (NodeDependency dep in deps)
            {
                ct.ThrowIfCancellationRequested();
                WbsNode? pred = await _wbsService.GetByIdAsync(dep.PredecessorId);
                    (string typeLabel, string typeDescription) = dep.Type switch
                    {
                        DependencyType.FinishToStart  => ("FS", "Predecessor must finish before this node can start"),
                        DependencyType.StartToStart   => ("SS", "Predecessor must start before this node can start"),
                        DependencyType.FinishToFinish => ("FF", "Predecessor must finish before this node can finish"),
                        DependencyType.StartToFinish  => ("SF", "Predecessor must start before this node can finish"),
                        _                             => (dep.Type.ToString(), string.Empty),
                    };

                WbsStatus predStatus = pred?.Status ?? WbsStatus.NotStarted;
                bool   isBlocking    = pred is not null && DependencyConstraints.IsViolated(dep.Type, predStatus);
                string blockingReason = DependencyConstraints.GetBlockingReason(dep.Type, predStatus);

                    NodeDependencies.Add(new DependencyRowViewModel
                    {
                        Dependency       = dep,
                        PredecessorCode  = pred?.Code ?? "?",
                        PredecessorTitle = pred?.Title ?? "(unknown)",
                        TypeLabel        = typeLabel,
                        TypeDescription  = typeDescription,
                        IsBlocking       = isBlocking,
                        BlockingReason   = blockingReason,
                    });
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Load Error", ex.Message);
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
            _cachedDependencies      = deps;
            BlockedNodeIds           = _wbsService.ResolveBlockedNodeIds(nodes, deps);
            CompletionBlockedNodeIds = _wbsService.ResolveCompletionBlockedNodeIds(nodes, deps);
            ViolatedCompleteNodeIds  = _wbsService.ResolveViolatedCompleteNodeIds(nodes, deps);
            ViolatedInProgressNodeIds = _wbsService.ResolveViolatedInProgressNodeIds(nodes, deps);
            DiagramDependencies      = deps;
            DiagramNodes             = nodes;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Load Error", ex.Message);
        }
    }

    /// <summary>
    /// Recomputes <see cref="BlockedNodeIds"/> immediately using the cached dependency list and
    /// the currently pending status edit, without waiting for the debounced DB save to complete.
    /// </summary>
    private void RefreshBlockedIds()
    {
        if (DiagramNodes is null || SelectedNode is null) return;

        WbsNode? target = DiagramNodes.FirstOrDefault(n => n.Id == SelectedNode.Node.Id);
        if (target is null) return;

        // Temporarily apply the pending status to the in-memory node so ResolveBlockedNodeIds
        // sees the new value; restore immediately after — synchronous so no race condition.
        WbsStatus original = target.Status;
        target.Status = EditStatus ?? WbsStatus.NotStarted;
        try
        {
            BlockedNodeIds            = _wbsService.ResolveBlockedNodeIds(DiagramNodes, _cachedDependencies);
            CompletionBlockedNodeIds  = _wbsService.ResolveCompletionBlockedNodeIds(DiagramNodes, _cachedDependencies);
            ViolatedCompleteNodeIds   = _wbsService.ResolveViolatedCompleteNodeIds(DiagramNodes, _cachedDependencies);
            ViolatedInProgressNodeIds = _wbsService.ResolveViolatedInProgressNodeIds(DiagramNodes, _cachedDependencies);
        }
        finally
        {
            target.Status = original;
        }
    }

    private async Task NotifyStatusBlockedAsync(
        string title,
        string intro,
        List<DependencyRowViewModel> violators)
    {
        string details = string.Join("\n", violators.Select(
            r => $"  • {r.PredecessorCode} {r.PredecessorTitle}: {r.BlockingReason}"));

        await _dialogService.ShowErrorAsync(title, $"{intro}\n\n{details}");
    }

    /// <summary>Selects a node from the diagram view, populating the detail panel.</summary>
    public void SelectDiagramNode(WbsNode node)
    {
        if (SelectedNode is { } currentNode)
        {
            _hasPendingEdit = false;
            _saveVersion++;
            IReadOnlySet<Guid> preSaveComplete   = DiagramNodes is not null
                ? _wbsService.ResolveViolatedCompleteNodeIds(DiagramNodes, _cachedDependencies)
                : (IReadOnlySet<Guid>)new HashSet<Guid>();
            IReadOnlySet<Guid> preSaveInProgress = DiagramNodes is not null
                ? _wbsService.ResolveViolatedInProgressNodeIds(DiagramNodes, _cachedDependencies)
                : (IReadOnlySet<Guid>)new HashSet<Guid>();
            ApplyEditFieldsToNode(currentNode);
            _ = FlushSaveAsync(currentNode, _pageCts?.Token ?? default, preSaveComplete, preSaveInProgress);
        }
        SelectedNode = new WbsNodeViewModel(node, hasChildren: false);
    }

    private async Task LoadAllProjectNodesAsync(CancellationToken ct = default)
    {
        AllProjectNodes.Clear();
        if (_projectStateService.ActiveProject is not { Id: var projectId }) return;
        try
        {
            ct.ThrowIfCancellationRequested();
            IReadOnlyList<WbsNode> nodes = await _wbsService.GetAllByProjectAsync(projectId);
            foreach (WbsNode node in nodes.Where(n => n.Id != SelectedNode?.Node.Id))
            {
                ct.ThrowIfCancellationRequested();
                AllProjectNodes.Add(node);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Load Error", ex.Message);
        }
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
            EditStartDate = node.StartDate.HasValue
                ? (DateTimeOffset?)new DateTimeOffset(DateTime.SpecifyKind(node.StartDate.Value, DateTimeKind.Utc))
                : null;
            EditDueDate = node.DueDate.HasValue
                ? (DateTimeOffset?)new DateTimeOffset(DateTime.SpecifyKind(node.DueDate.Value, DateTimeKind.Utc))
                : null;
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
        if (_saveVersion != version || SelectedNode is null) return;
        _hasPendingEdit = false;
        await SaveNodeAsync();
    }

    private async Task LoadLinkedMilestonesAsync(Guid nodeId, CancellationToken ct = default)
    {
        LinkedMilestones.Clear();
        if (_projectStateService.ActiveProject is not { Id: var projectId }) return;
        try
        {
            ct.ThrowIfCancellationRequested();
            IReadOnlyList<Milestone> milestones = await _milestoneService.GetByProjectAsync(projectId);
            foreach (Milestone m in milestones)
            {
                ct.ThrowIfCancellationRequested();
                List<Guid> ids = JsonSerializer.Deserialize<List<Guid>>(m.LinkedNodeIds) ?? [];
                if (!ids.Contains(nodeId)) continue;
                LinkedMilestones.Add(new MilestoneRowViewModel
                {
                    Milestone      = m,
                    DueDateLabel   = DateTime.SpecifyKind(m.DueDate, DateTimeKind.Utc).ToString("d MMM yyyy"),
                    LinkedNodeCount = ids.Count,
                });
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Load Error", ex.Message);
        }
    }
}

