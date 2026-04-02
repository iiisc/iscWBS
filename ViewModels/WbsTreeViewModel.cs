using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iscWBS.Core.Models;
using iscWBS.Core.Services;

namespace iscWBS.ViewModels;

public sealed partial class WbsTreeViewModel : ObservableObject, INavigationAware
{
    private readonly IWbsService _wbsService;
    private readonly IProjectStateService _projectStateService;
    private readonly IDialogService _dialogService;

    public ObservableCollection<WbsNodeViewModel> RootNodes { get; } = new();
    public ObservableCollection<DependencyRowViewModel> NodeDependencies { get; } = new();
    public ObservableCollection<WbsNode> AllProjectNodes { get; } = new();

    /// <summary>Raised after a child node is added so the view can expand the parent <see cref="TreeViewNode"/>.</summary>
    public event EventHandler<WbsNodeViewModel>? ExpandNodeRequested;

    public IReadOnlyList<DependencyTypeOption> DependencyTypeOptions { get; } =
    [
        new(DependencyType.FinishToStart, "Finish → Start"),
        new(DependencyType.StartToStart, "Start → Start"),
        new(DependencyType.FinishToFinish, "Finish → Finish"),
    ];

    [ObservableProperty]
    public partial WbsNodeViewModel? SelectedNode { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

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

    partial void OnEditStatusChanged(WbsStatus? value) => OnPropertyChanged(nameof(EditStatusIndex));

    [ObservableProperty]
    public partial double EditEstimatedHours { get; set; }

    [ObservableProperty]
    public partial double EditActualHours { get; set; }

    [ObservableProperty]
    public partial double EditEstimatedCost { get; set; }

    [ObservableProperty]
    public partial double EditActualCost { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset? EditStartDate { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset? EditDueDate { get; set; }

    [ObservableProperty]
    public partial WbsNode? NewDepPredecessor { get; set; }

    [ObservableProperty]
    public partial DependencyTypeOption? NewDepTypeOption { get; set; } =
        new(DependencyType.FinishToStart, "Finish → Start");

    public bool IsDetailVisible => SelectedNode is not null;

    public IReadOnlyList<WbsStatus> StatusOptions { get; } = Enum.GetValues<WbsStatus>().ToList();

    public WbsTreeViewModel(
        IWbsService wbsService,
        IProjectStateService projectStateService,
        IDialogService dialogService)
    {
        _wbsService = wbsService;
        _projectStateService = projectStateService;
        _dialogService = dialogService;
    }

    public void OnNavigatedTo(object? parameter) => _ = LoadRootNodesAsync();
    public void OnNavigatedFrom() { }

    partial void OnSelectedNodeChanged(WbsNodeViewModel? value)
    {
        OnPropertyChanged(nameof(IsDetailVisible));
        if (value is null)
        {
            NodeDependencies.Clear();
            AllProjectNodes.Clear();
            return;
        }
        LoadEditFieldsFromNode(value.Node);
        _ = LoadDependenciesAsync(value.Node.Id);
        _ = LoadAllProjectNodesAsync();
    }

    [RelayCommand]
    private async Task LoadRootNodesAsync()
    {
        if (_projectStateService.ActiveProject is not { Id: var projectId }) return;

        IsLoading = true;
        try
        {
            RootNodes.Clear();
            IReadOnlyList<WbsNode> roots = await _wbsService.GetRootNodesAsync(projectId);
            foreach (WbsNode node in roots)
            {
                bool hasChildren = await _wbsService.HasChildrenAsync(node.Id);
                RootNodes.Add(new WbsNodeViewModel(node, hasChildren));
            }
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Load Error", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ExpandNodeAsync(WbsNodeViewModel nodeVm)
    {
        if (nodeVm.Children.Count != 1 || nodeVm.Children[0] != WbsNodeViewModel.Placeholder)
            return;

        try
        {
            IReadOnlyList<WbsNode> children = await _wbsService.GetChildrenAsync(nodeVm.Node.Id);
            foreach (WbsNode child in children)
            {
                bool hasChildren = await _wbsService.HasChildrenAsync(child.Id);
                nodeVm.Children.Add(new WbsNodeViewModel(child, hasChildren));
            }
            nodeVm.Children.Remove(WbsNodeViewModel.Placeholder);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Load Error", ex.Message);
        }
    }

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
            WbsNodeViewModel[] oldChildren = nodeVm.Children.ToArray();
            IReadOnlyList<WbsNode> children = await _wbsService.GetChildrenAsync(nodeVm.Node.Id);
            foreach (WbsNode child in children)
            {
                bool hasChildren = await _wbsService.HasChildrenAsync(child.Id);
                nodeVm.Children.Add(new WbsNodeViewModel(child, hasChildren));
            }
            foreach (WbsNodeViewModel old in oldChildren)
                nodeVm.Children.Remove(old);
            ExpandNodeRequested?.Invoke(this, nodeVm);
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
    private void BeginEditNode(WbsNodeViewModel? nodeVm)
    {
        if (nodeVm is null) return;
        nodeVm.IsEditing = true;
    }

    [RelayCommand]
    private async Task CommitInlineEditAsync(WbsNodeViewModel nodeVm)
    {
        nodeVm.IsEditing = false;
        nodeVm.Node.Title = nodeVm.Title;
        try
        {
            await _wbsService.UpdateNodeAsync(nodeVm.Node);
            if (SelectedNode?.Node.Id == nodeVm.Node.Id)
                EditTitle = nodeVm.Title;
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Save Error", ex.Message);
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
            if (SelectedNode == nodeVm) SelectedNode = null;
            await LoadRootNodesAsync();
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Delete Error", ex.Message);
        }
    }

    [RelayCommand]
    private async Task MoveNodeUpAsync(WbsNodeViewModel? nodeVm)
    {
        if (nodeVm is null || nodeVm.Node.SortOrder == 0) return;
        try
        {
            await _wbsService.MoveNodeAsync(nodeVm.Node.Id, nodeVm.Node.ParentId, nodeVm.Node.SortOrder - 1);
            await LoadRootNodesAsync();
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Move Error", ex.Message);
        }
    }

    [RelayCommand]
    private async Task MoveNodeDownAsync(WbsNodeViewModel? nodeVm)
    {
        if (nodeVm is null) return;
        try
        {
            await _wbsService.MoveNodeAsync(nodeVm.Node.Id, nodeVm.Node.ParentId, nodeVm.Node.SortOrder + 1);
            await LoadRootNodesAsync();
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Move Error", ex.Message);
        }
    }

    [RelayCommand]
    private async Task SaveNodeAsync()
    {
        if (SelectedNode is null) return;

        SelectedNode.Node.Title = EditTitle;
        SelectedNode.Node.Description = EditDescription;
        SelectedNode.Node.AssignedTo = EditAssignedTo;
        SelectedNode.Node.Status = EditStatus ?? WbsStatus.NotStarted;
        SelectedNode.Node.EstimatedHours = EditEstimatedHours;
        SelectedNode.Node.ActualHours = EditActualHours;
        SelectedNode.Node.EstimatedCost = EditEstimatedCost;
        SelectedNode.Node.ActualCost = EditActualCost;
        SelectedNode.Node.StartDate = EditStartDate?.UtcDateTime;
        SelectedNode.Node.DueDate = EditDueDate?.UtcDateTime;
        SelectedNode.Title = EditTitle;

        try
        {
            await _wbsService.UpdateNodeAsync(SelectedNode.Node);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Save Error", ex.Message);
        }
    }

    [RelayCommand]
    private void CancelEdit()
    {
        if (SelectedNode is null) return;
        LoadEditFieldsFromNode(SelectedNode.Node);
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

    private async Task LoadDependenciesAsync(Guid nodeId)
    {
        NodeDependencies.Clear();
        NewDepPredecessor = null;
        try
        {
            IReadOnlyList<NodeDependency> deps = await _wbsService.GetDependenciesAsync(nodeId);
            foreach (NodeDependency dep in deps)
            {
                WbsNode? pred = await _wbsService.GetByIdAsync(dep.PredecessorId);
                string typeLabel = dep.Type switch
                {
                    DependencyType.FinishToStart => "FS",
                    DependencyType.StartToStart  => "SS",
                    DependencyType.FinishToFinish => "FF",
                    _                            => dep.Type.ToString()
                };
                NodeDependencies.Add(new DependencyRowViewModel
                {
                    Dependency = dep,
                    PredecessorCode = pred?.Code ?? "?",
                    PredecessorTitle = pred?.Title ?? "(unknown)",
                    TypeLabel = typeLabel
                });
            }
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Load Error", ex.Message);
        }
    }

    private async Task LoadAllProjectNodesAsync()
    {
        AllProjectNodes.Clear();
        if (_projectStateService.ActiveProject is not { Id: var projectId }) return;
        try
        {
            IReadOnlyList<WbsNode> nodes = await _wbsService.GetAllByProjectAsync(projectId);
            foreach (WbsNode node in nodes.Where(n => n.Id != SelectedNode?.Node.Id))
                AllProjectNodes.Add(node);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Load Error", ex.Message);
        }
    }

    private void LoadEditFieldsFromNode(WbsNode node)
    {
        EditTitle = node.Title;
        EditDescription = node.Description;
        EditAssignedTo = node.AssignedTo;
        EditStatus = node.Status;
        EditEstimatedHours = node.EstimatedHours;
        EditActualHours = node.ActualHours;
        EditEstimatedCost = node.EstimatedCost;
        EditActualCost = node.ActualCost;
        EditStartDate = node.StartDate.HasValue
            ? (DateTimeOffset?)new DateTimeOffset(DateTime.SpecifyKind(node.StartDate.Value, DateTimeKind.Utc))
            : null;
        EditDueDate = node.DueDate.HasValue
            ? (DateTimeOffset?)new DateTimeOffset(DateTime.SpecifyKind(node.DueDate.Value, DateTimeKind.Utc))
            : null;
    }
}

