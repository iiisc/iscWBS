using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iscWBS.Core.Models;
using iscWBS.Core.Services;

namespace iscWBS.ViewModels;

public sealed partial class WbsOutlineViewModel : ObservableObject, INavigationAware
{
    private readonly IWbsService _wbsService;
    private readonly IProjectStateService _projectStateService;
    private readonly IDialogService _dialogService;
    private readonly INavigationService _navigationService;

    public ObservableCollection<WbsNode> Nodes { get; } = new();

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    public WbsOutlineViewModel(
        IWbsService wbsService,
        IProjectStateService projectStateService,
        IDialogService dialogService,
        INavigationService navigationService)
    {
        _wbsService = wbsService;
        _projectStateService = projectStateService;
        _dialogService = dialogService;
        _navigationService = navigationService;
    }

    public void OnNavigatedTo(object? parameter) => _ = LoadNodesAsync();
    public void OnNavigatedFrom() { }

    [RelayCommand]
    private async Task LoadNodesAsync()
    {
        if (_projectStateService.ActiveProject is not { Id: var projectId }) return;

        IsLoading = true;
        try
        {
            Nodes.Clear();
            IReadOnlyList<WbsNode> all = await _wbsService.GetAllByProjectAsync(projectId);
            foreach (WbsNode node in all)
                Nodes.Add(node);
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
    private void SelectNode(WbsNode? node)
    {
        if (node is null) return;
        _navigationService.NavigateTo("WbsTreePage");
    }
}

