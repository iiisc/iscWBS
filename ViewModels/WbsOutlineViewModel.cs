using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iscWBS.Core.Models;
using iscWBS.Core.Services;
using iscWBS.Helpers;

namespace iscWBS.ViewModels;

public sealed partial class WbsOutlineViewModel : ObservableObject
{
    private readonly WbsService _wbsService;
    private readonly ProjectStateService _projectStateService;
    private readonly DialogService _dialogService;
    private readonly NavigationService _navigationService;

    public ObservableCollection<WbsOutlineRowViewModel> Nodes { get; } = new();

    [ObservableProperty]
    public partial bool HasNoNodes { get; private set; }

    public WbsOutlineViewModel(
        WbsService wbsService,
        ProjectStateService projectStateService,
        DialogService dialogService,
        NavigationService navigationService)
    {
        _wbsService = wbsService;
        _projectStateService = projectStateService;
        _dialogService = dialogService;
        _navigationService = navigationService;
    }

    public void OnNavigatedTo() => _ = LoadNodesAsync();

    private async Task LoadNodesAsync()
    {
        if (_projectStateService.ActiveProject is not { Id: var projectId }) return;

        try
        {
            Nodes.Clear();
            IReadOnlyList<WbsNode> all = await _wbsService.GetAllByProjectAsync(projectId);
            IReadOnlyList<NodeDependency> deps = await _wbsService.GetAllDependenciesByProjectAsync(projectId);
            IReadOnlySet<Guid> blockedIds = DependencyConstraints.BlockedNodeIds(all, deps);
            Dictionary<Guid, int> levels = new WbsHierarchy(all).BranchLevels();

            foreach (WbsNode node in all)
                Nodes.Add(new WbsOutlineRowViewModel
                {
                    Node            = node,
                    EffectiveStatus = DependencyConstraints.EffectiveStatus(node, blockedIds),
                    Level           = levels.GetValueOrDefault(node.Id, 1),
                });
            HasNoNodes = Nodes.Count == 0;
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("ErrorTitle_Load", ex);
        }
    }

    [RelayCommand]
    private void OpenTree() => _navigationService.NavigateTo("WbsTreePage");

    /// <summary>Navigates to the WBS tree, selecting the node from the given outline row.</summary>
    [RelayCommand]
    private void SelectRow(WbsOutlineRowViewModel? row)
    {
        if (row is not null)
            _navigationService.NavigateTo("WbsTreePage", row.Node.Id);
    }
}

