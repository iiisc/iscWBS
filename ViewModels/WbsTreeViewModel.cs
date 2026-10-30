using System.Collections.ObjectModel;
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
    private Task _saveTask = Task.CompletedTask;
    private Guid _pendingSelectNodeId;
    private CancellationTokenSource? _pageCts;

    public List<DependencyTypeOption> DependencyTypeOptions { get; } =
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

    /// <summary>Null while the status box is between item lists, which happens when the selection changes.</summary>
    [ObservableProperty]
    public partial WbsStatus? EditStatus { get; set; }

    partial void OnEditStatusChanged(WbsStatus? value)
    {
        if (_isLoadingEditFields || SelectedNode is not { } node || value is not { } status) return;

        IReadOnlyList<NodeDependency> blockers = DependencyConstraints.TransitionBlockers(
            status,
            DiagramDependencies.Where(d => d.SuccessorId == node.Id),
            (DiagramNodes ?? []).ToDictionary(n => n.Id, n => n.Status));

        if (blockers.Count == 0)
        {
            RefreshBlockedIds();
            SaveNow();
            return;
        }

        _isLoadingEditFields = true;
        try   { EditStatus = node.Status; }
        finally { _isLoadingEditFields = false; }

        // x:Bind TwoWay suppresses PropertyChanged for EditStatus while the ComboBox SelectionChanged
        // dispatch is still on the stack, so re-notify once it clears for the ComboBox to show the reverted value.
        DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() => OnPropertyChanged(nameof(EditStatus)));

        bool starting = status == WbsStatus.InProgress;
        string details = string.Join("\n", NodeDependencies
            .Where(r => blockers.Any(b => b.Id == r.Dependency.Id))
            .Select(r => $"  • {r.PredecessorCode} {r.PredecessorTitle}: {r.BlockingReason}"));
        _ = _dialogService.ShowErrorAsync(
            Strings.Get(starting ? "Wbs_CannotStartTitle" : "Wbs_CannotCompleteTitle"),
            $"{Strings.Get(starting ? "Wbs_CannotStartIntro" : "Wbs_CannotCompleteIntro")}\n\n{details}");
    }

    // Text is saved when its box loses focus (SaveIfDirty), not per keystroke; the other inputs commit once.
    partial void OnEditTitleChanged(string value) => MarkDirty();
    partial void OnEditDescriptionChanged(string value) => MarkDirty();
    partial void OnEditAssignedToChanged(string value) => MarkDirty();
    partial void OnEditEstimatedHoursChanged(double value) => SaveNow();
    partial void OnEditActualHoursChanged(double value) => SaveNow();
    partial void OnEditStartDateChanged(DateTimeOffset? value) => SaveNow();
    partial void OnEditDueDateChanged(DateTimeOffset? value) => SaveNow();
    partial void OnEditIsDeliverableChanged(bool value) => SaveNow();

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
    public partial List<DependencyRowViewModel> NodeDependencies { get; private set; } = [];

    /// <summary>Every node but <see cref="SelectedNode"/>, for the add-predecessor picker.</summary>
    [ObservableProperty]
    public partial List<WbsNode> PredecessorOptions { get; private set; } = [];

    [ObservableProperty]
    public partial List<MilestoneRowViewModel> LinkedMilestones { get; private set; } = [];

    public bool HasNoNodes => DiagramNodes is null || DiagramNodes.Count == 0;

    partial void OnDiagramNodesChanged(IReadOnlyList<WbsNode>? value)
    {
        _hierarchy = new WbsHierarchy(value ?? []);
        OnPropertyChanged(nameof(HasNoNodes));
        NotifyMoveCommands();
    }

    private void NotifyMoveCommands()
    {
        MoveEarlierCommand.NotifyCanExecuteChanged();
        MoveLaterCommand.NotifyCanExecuteChanged();
        IndentCommand.NotifyCanExecuteChanged();
        OutdentCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Shown after an edit moved other work later to keep its dependencies; null when there is nothing to tell.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRescheduleNotice))]
    public partial string? RescheduleNotice { get; set; }

    public bool HasRescheduleNotice
    {
        get => RescheduleNotice is not null;
        set { if (!value) RescheduleNotice = null; }
    }

    private void ReportRescheduled(IReadOnlyList<WbsNode> moved)
    {
        if (moved.Count > 0)
            RescheduleNotice = string.Join(", ", moved);
    }

    /// <summary>Nodes whose code or title contains the search text, for the search box's suggestions.</summary>
    [ObservableProperty]
    public partial List<WbsNode> SearchResults { get; private set; } = [];

    public void Search(string text)
    {
        string query = text.Trim();
        SearchResults = query.Length == 0 ? [] : (DiagramNodes ?? [])
            .Where(n => n.Code.StartsWith(query, StringComparison.OrdinalIgnoreCase)
                     || n.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .Take(12)
            .ToList();
    }

    public ObservableCollection<NodeNote> Notes { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddNoteCommand))]
    public partial string NewNoteText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddDependencyCommand))]
    public partial WbsNode? NewDepPredecessor { get; set; }

    [ObservableProperty]
    public partial DependencyTypeOption? NewDepTypeOption { get; set; }

    [ObservableProperty]
    public partial double NewDepLagDays { get; set; }

    public bool IsDetailVisible => SelectedNode is not null;

    /// <summary>The id of <see cref="SelectedNode"/> for the diagram highlight, or <see cref="Guid.Empty"/> when nothing is selected.</summary>
    public Guid SelectedNodeId => SelectedNode?.Id ?? Guid.Empty;

    private static readonly List<WbsStatus> _settableStatuses = Enum.GetValues<WbsStatus>().Except([WbsStatus.Blocked]).ToList();
    private static readonly List<WbsStatus> _allStatuses = [.. Enum.GetValues<WbsStatus>()];

    /// <summary>Blocked can't be chosen, but files from earlier versions can hold it, and the box has to be able to show it.</summary>
    [ObservableProperty]
    public partial List<WbsStatus> StatusOptions { get; private set; } = _settableStatuses;

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
        SaveIfDirty();
        _pageCts?.Cancel();
        _pageCts?.Dispose();
        _pageCts = null;
    }

    partial void OnSelectedNodeChanged(WbsNode? value)
    {
        _hasPendingEdit = false;
        OnPropertyChanged(nameof(IsDetailVisible));
        OnPropertyChanged(nameof(SelectedNodeId));
        NotifyMoveCommands();
        AddNoteCommand.NotifyCanExecuteChanged();
        NewDepPredecessor = null;
        NewNoteText = string.Empty;
        RefreshSelectionLists();
        LinkedMilestones = [];
        Notes.Clear();
        if (value is null) return;
        LoadEditFieldsFromNode(value);
        _ = LoadLinkedMilestonesAsync(value.Id);
        _ = LoadNotesAsync(value.Id);
    }

    private WbsHierarchy _hierarchy = new([]);

    /// <summary>The node's siblings in order, and its place among them.</summary>
    private (IReadOnlyList<WbsNode> Siblings, int Index) Position(WbsNode node)
    {
        IReadOnlyList<WbsNode> siblings = node.ParentId is { } parent ? _hierarchy.ChildrenOf(parent) : _hierarchy.Roots;
        return (siblings, siblings.TakeWhile(n => n.Id != node.Id).Count());
    }

    private bool CanMoveEarlier => SelectedNode is { } node && Position(node).Index > 0;
    private bool CanMoveLater => SelectedNode is { } node && Position(node) is var (siblings, index) && index < siblings.Count - 1;
    private bool CanOutdent => SelectedNode?.ParentId is not null;

    [RelayCommand(CanExecute = nameof(CanMoveEarlier))]
    private Task MoveEarlierAsync() => MoveSelectedAsync(node => (node.ParentId, Position(node).Index - 1));

    [RelayCommand(CanExecute = nameof(CanMoveLater))]
    private Task MoveLaterAsync() => MoveSelectedAsync(node => (node.ParentId, Position(node).Index + 1));

    /// <summary>Makes the node the last child of the sibling before it.</summary>
    [RelayCommand(CanExecute = nameof(CanMoveEarlier))]
    private Task IndentAsync() => MoveSelectedAsync(node =>
    {
        (IReadOnlyList<WbsNode> siblings, int index) = Position(node);
        return (siblings[index - 1].Id, int.MaxValue);
    });

    /// <summary>Makes the node the sibling right after its parent.</summary>
    [RelayCommand(CanExecute = nameof(CanOutdent))]
    private Task OutdentAsync() => MoveSelectedAsync(node =>
    {
        WbsNode parent = DiagramNodes!.First(n => n.Id == node.ParentId);
        return (parent.ParentId, Position(parent).Index + 1);
    });

    private async Task MoveSelectedAsync(Func<WbsNode, (Guid? Parent, int Index)> target)
    {
        if (SelectedNode is not { } node) return;
        SaveIfDirty();
        await _saveTask;
        (Guid? parent, int index) = target(node);
        await MoveAsync(node.Id, parent, index);
    }

    /// <summary>Moves a node, dragged in the diagram, to be the last child of <paramref name="newParentId"/>.</summary>
    public Task MoveIntoAsync(Guid nodeId, Guid newParentId) => MoveAsync(nodeId, newParentId, int.MaxValue);

    private async Task MoveAsync(Guid nodeId, Guid? parent, int index)
    {
        try
        {
            await _wbsService.MoveNodeAsync(nodeId, parent, index);
            _pendingSelectNodeId = nodeId;
            await LoadDiagramNodesAsync(PageToken);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("ErrorTitle_Move", ex);
        }
    }

    private bool CanAddNote => SelectedNode is not null && !string.IsNullOrWhiteSpace(NewNoteText);

    [RelayCommand(CanExecute = nameof(CanAddNote))]
    private async Task AddNoteAsync()
    {
        if (SelectedNode is not { } node) return;
        try
        {
            NodeNote note = await _wbsService.AddNoteAsync(node.Id, NewNoteText);
            Notes.Insert(0, note);
            NewNoteText = string.Empty;
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("ErrorTitle_Save", ex);
        }
    }

    [RelayCommand]
    private async Task DeleteNoteAsync(NodeNote? note)
    {
        if (note is null) return;
        try
        {
            await _wbsService.DeleteNoteAsync(note.Id);
            Notes.Remove(note);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("ErrorTitle_Delete", ex);
        }
    }

    private async Task LoadNotesAsync(Guid nodeId)
    {
        try
        {
            IReadOnlyList<NodeNote> notes = await _wbsService.GetNotesAsync(nodeId);
            if (SelectedNode?.Id != nodeId) return;
            Notes.Clear();
            foreach (NodeNote note in notes)
                Notes.Add(note);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("ErrorTitle_Load", ex);
        }
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
            _pendingSelectNodeId = (await add(Strings.Get("Wbs_NewNodeTitle"))).Id;
            await LoadDiagramNodesAsync(PageToken);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync(errorTitleKey, ex);
        }
    }

    [RelayCommand]
    private async Task DuplicateNodeAsync(WbsNode? node)
    {
        if (node is not null)
            await AddNodeAsync(_ => _wbsService.DuplicateNodeAsync(node.Id), "ErrorTitle_Duplicate");
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
        SaveIfDirty();
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
        node.Status         = EditStatus ?? node.Status;
        // An emptied NumberBox holds NaN, which SQLite refuses to store.
        node.EstimatedHours = double.IsNaN(EditEstimatedHours) ? 0 : EditEstimatedHours;
        node.ActualHours    = double.IsNaN(EditActualHours) ? 0 : EditActualHours;
        node.StartDate      = EditStartDate?.Date;
        node.DueDate        = EditDueDate?.Date;
        node.IsDeliverable  = EditIsDeliverable;

        try
        {
            IReadOnlyList<WbsNode> moved = await _wbsService.UpdateNodeAsync(node);
            await LoadDiagramNodesAsync(PageToken);
            ReportRescheduled(moved);

            await WarnNewlyViolatedAsync(ViolatedCompleteNodeIds, completeBefore,
                "Wbs_CompleteViolatedTitle", "Wbs_CompleteViolatedIntro", "Wbs_CompleteViolatedOutro");

            await WarnNewlyViolatedAsync(ViolatedInProgressNodeIds, inProgressBefore,
                "Wbs_InProgressViolatedTitle", "Wbs_InProgressViolatedIntro", "Wbs_InProgressViolatedOutro");
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("ErrorTitle_Save", ex);
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

    private bool CanAddDependency => SelectedNode is not null && NewDepPredecessor is not null;

    [RelayCommand(CanExecute = nameof(CanAddDependency))]
    private async Task AddDependencyAsync()
    {
        if (SelectedNode is null || NewDepPredecessor is null) return;

        var dep = new NodeDependency
        {
            PredecessorId = NewDepPredecessor.Id,
            SuccessorId = SelectedNode.Id,
            Type = NewDepTypeOption?.Type ?? DependencyType.FinishToStart,
            // A cleared NumberBox gives NaN.
            LagDays = double.IsNaN(NewDepLagDays) ? 0 : (int)NewDepLagDays
        };
        try
        {
            IReadOnlyList<WbsNode> moved = await _wbsService.AddDependencyAsync(dep);
            NewDepPredecessor = null;
            NewDepLagDays = 0;
            await LoadDiagramNodesAsync(PageToken);
            ReportRescheduled(moved);
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
                    TypeLabel        = DependencyConstraints.GetAbbreviation(d.Type) + (d.LagDays == 0 ? "" : $" {d.LagDays:+0;-0} d"),
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
        target.Status = EditStatus ?? original;
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
        SaveIfDirty();
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
            StatusOptions = node.Status == WbsStatus.Blocked ? _allStatuses : _settableStatuses;
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

    private void MarkDirty()
    {
        if (!_isLoadingEditFields && SelectedNode is not null)
            _hasPendingEdit = true;
    }

    private void SaveNow()
    {
        MarkDirty();
        SaveIfDirty();
    }

    /// <summary>Saves the selected node if it has unsaved edits; called on blur, before the selection or page changes, and on close.</summary>
    public void SaveIfDirty()
    {
        if (!_hasPendingEdit) return;
        _hasPendingEdit = false;
        _saveTask = SaveNodeAsync();
    }

    /// <summary>Saves pending edits and waits until they are written.</summary>
    public Task FlushAsync()
    {
        SaveIfDirty();
        return _saveTask;
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
