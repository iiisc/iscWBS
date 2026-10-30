using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iscWBS.Core.Models;
using iscWBS.Core.Services;
using iscWBS.Helpers;

namespace iscWBS.ViewModels;

public sealed partial class MilestonesViewModel : ObservableObject
{
    private readonly MilestoneService _milestoneService;
    private readonly WbsService _wbsService;
    private readonly ProjectStateService _projectStateService;
    private readonly DialogService _dialogService;

    public ObservableCollection<MilestoneRowViewModel> Milestones { get; } = new();
    public ObservableCollection<WbsNode> LinkedNodes { get; } = new();

    // WBS nodes aren't edited on this page, so one load per visit serves every selection.
    [ObservableProperty]
    public partial List<WbsNode> AllProjectNodes { get; private set; } = [];

    [ObservableProperty]
    public partial MilestoneRowViewModel? SelectedMilestone { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddMilestoneCommand))]
    public partial string NewTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial DateTimeOffset? NewDueDate { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveEditCommand))]
    public partial string EditTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial DateTimeOffset? EditDueDate { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddLinkedNodeCommand))]
    public partial WbsNode? NewLinkedNode { get; set; }

    public bool IsDetailVisible => SelectedMilestone is not null;
    public bool IsSelectedMilestoneIncomplete => SelectedMilestone?.Milestone.IsComplete == false;

    public MilestonesViewModel(
        MilestoneService milestoneService,
        WbsService wbsService,
        ProjectStateService projectStateService,
        DialogService dialogService)
    {
        _milestoneService = milestoneService;
        _wbsService = wbsService;
        _projectStateService = projectStateService;
        _dialogService = dialogService;
    }

    public void OnNavigatedTo() => _ = LoadAsync();

    partial void OnSelectedMilestoneChanged(MilestoneRowViewModel? value)
    {
        SaveEditCommand.NotifyCanExecuteChanged();
        AddLinkedNodeCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsDetailVisible));
        OnPropertyChanged(nameof(IsSelectedMilestoneIncomplete));
        if (value is null)
        {
            LinkedNodes.Clear();
            return;
        }
        EditTitle = value.Milestone.Title;
        EditDueDate = CalendarDates.ToPickerValue(value.Milestone.DueDate);
        _ = LoadLinkedNodesAsync(value.Milestone);
    }

    private async Task LoadAsync()
    {
        if (_projectStateService.ActiveProject is not { Id: var projectId }) return;
        try
        {
            AllProjectNodes = [.. await _wbsService.GetAllByProjectAsync(projectId)];
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("ErrorTitle_Load", ex);
        }
        await LoadMilestonesAsync();
    }

    /// <summary>Reloads the list (link counts included) and reselects the current milestone.</summary>
    private async Task ReloadKeepingSelectionAsync()
    {
        Guid? selectedId = SelectedMilestone?.Milestone.Id;
        // Reselecting loads the saved title and date, which would throw away edits not saved yet.
        (string title, DateTimeOffset? dueDate) = (EditTitle, EditDueDate);
        await LoadMilestonesAsync();
        SelectedMilestone = Milestones.FirstOrDefault(r => r.Milestone.Id == selectedId);
        if (SelectedMilestone is not null)
            (EditTitle, EditDueDate) = (title, dueDate);
    }

    private async Task LoadMilestonesAsync()
    {
        if (_projectStateService.ActiveProject is not { Id: var projectId }) return;
        try
        {
            Milestones.Clear();
            IReadOnlyList<Milestone> milestones = await _milestoneService.GetByProjectAsync(projectId);
            IReadOnlyDictionary<Guid, int> counts =
                await _milestoneService.GetLinkedCountsByProjectAsync(projectId);

            foreach (Milestone m in milestones)
            {
                Milestones.Add(new MilestoneRowViewModel
                {
                    Milestone = m,
                    LinkedNodeCount = counts.TryGetValue(m.Id, out int c) ? c : 0
                });
            }
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("ErrorTitle_Load", ex);
        }
    }

    private bool CanAddMilestone => !string.IsNullOrWhiteSpace(NewTitle);

    [RelayCommand(CanExecute = nameof(CanAddMilestone))]
    private async Task AddMilestoneAsync()
    {
        if (_projectStateService.ActiveProject is not { Id: var projectId }) return;
        if (string.IsNullOrWhiteSpace(NewTitle)) return;

        DateTime dueDate = NewDueDate?.Date ?? DateTime.Today.AddDays(7);
        try
        {
            await _milestoneService.CreateAsync(projectId, NewTitle, dueDate);
            NewTitle = string.Empty;
            NewDueDate = null;
            await ReloadKeepingSelectionAsync();
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("ErrorTitle_AddMilestone", ex);
        }
    }

    private bool CanSaveEdit => SelectedMilestone is not null && !string.IsNullOrWhiteSpace(EditTitle);

    [RelayCommand(CanExecute = nameof(CanSaveEdit))]
    private async Task SaveEditAsync()
    {
        if (SelectedMilestone is null || string.IsNullOrWhiteSpace(EditTitle)) return;

        SelectedMilestone.Milestone.Title = EditTitle.Trim();
        if (EditDueDate.HasValue)
            SelectedMilestone.Milestone.DueDate = EditDueDate.Value.Date;
        try
        {
            await _milestoneService.UpdateAsync(SelectedMilestone.Milestone);
            await ReloadKeepingSelectionAsync();
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("ErrorTitle_Save", ex);
        }
    }

    [RelayCommand]
    private async Task MarkCompleteAsync(MilestoneRowViewModel? row)
    {
        if (row is null || row.Milestone.IsComplete) return;

        bool confirmed = await _dialogService.ShowConfirmAsync(
            Strings.Get("Milestones_MarkCompleteTitle"),
            Strings.Format("Milestones_MarkCompleteConfirm", row.Milestone.Title));
        if (!confirmed) return;

        try
        {
            await _milestoneService.MarkCompleteAsync(row.Milestone.Id);
            SelectedMilestone = null;
            await LoadMilestonesAsync();
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("ErrorTitle_MarkComplete", ex);
        }
    }

    [RelayCommand]
    private async Task DeleteMilestoneAsync(MilestoneRowViewModel? row)
    {
        if (row is null) return;

        bool confirmed = await _dialogService.ShowConfirmAsync(
            Strings.Get("Milestones_DeleteTitle"),
            Strings.Format("Milestones_DeleteConfirm", row.Milestone.Title));
        if (!confirmed) return;

        try
        {
            await _milestoneService.DeleteAsync(row.Milestone.Id);
            if (SelectedMilestone == row) SelectedMilestone = null;
            await LoadMilestonesAsync();
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("ErrorTitle_Delete", ex);
        }
    }

    private bool CanAddLinkedNode => SelectedMilestone is not null && NewLinkedNode is not null;

    [RelayCommand(CanExecute = nameof(CanAddLinkedNode))]
    private async Task AddLinkedNodeAsync()
    {
        if (SelectedMilestone is null || NewLinkedNode is null) return;
        try
        {
            await _milestoneService.LinkNodeAsync(SelectedMilestone.Milestone.Id, NewLinkedNode.Id);
            await ReloadKeepingSelectionAsync();
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("ErrorTitle_Link", ex);
        }
    }

    [RelayCommand]
    private async Task RemoveLinkedNodeAsync(WbsNode? node)
    {
        if (node is null || SelectedMilestone is null) return;
        try
        {
            await _milestoneService.UnlinkNodeAsync(SelectedMilestone.Milestone.Id, node.Id);
            await ReloadKeepingSelectionAsync();
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("ErrorTitle_Unlink", ex);
        }
    }

    private async Task LoadLinkedNodesAsync(Milestone milestone)
    {
        LinkedNodes.Clear();
        NewLinkedNode = null;
        try
        {
            IReadOnlyList<Guid> ids = await _milestoneService.GetLinkedNodeIdsAsync(milestone.Id);
            Dictionary<Guid, WbsNode> nodesById = AllProjectNodes.ToDictionary(n => n.Id);
            foreach (Guid id in ids)
            {
                if (nodesById.TryGetValue(id, out WbsNode? node)) LinkedNodes.Add(node);
            }
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("ErrorTitle_Load", ex);
        }
    }
}
