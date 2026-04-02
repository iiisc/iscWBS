using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iscWBS.Core.Models;
using iscWBS.Core.Services;

namespace iscWBS.ViewModels;

public sealed partial class MilestonesViewModel : ObservableObject, INavigationAware
{
    private readonly IMilestoneService _milestoneService;
    private readonly IWbsService _wbsService;
    private readonly IProjectStateService _projectStateService;
    private readonly IDialogService _dialogService;

    public ObservableCollection<MilestoneRowViewModel> Milestones { get; } = new();
    public ObservableCollection<WbsNode> LinkedNodes { get; } = new();
    public ObservableCollection<WbsNode> AllProjectNodes { get; } = new();

    [ObservableProperty]
    public partial MilestoneRowViewModel? SelectedMilestone { get; set; }

    [ObservableProperty]
    public partial string NewTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial DateTimeOffset? NewDueDate { get; set; }

    [ObservableProperty]
    public partial string EditTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial DateTimeOffset? EditDueDate { get; set; }

    [ObservableProperty]
    public partial WbsNode? NewLinkedNode { get; set; }

    public bool IsDetailVisible => SelectedMilestone is not null;
    public bool IsSelectedMilestoneIncomplete => SelectedMilestone?.Milestone.IsComplete == false;

    public MilestonesViewModel(
        IMilestoneService milestoneService,
        IWbsService wbsService,
        IProjectStateService projectStateService,
        IDialogService dialogService)
    {
        _milestoneService = milestoneService;
        _wbsService = wbsService;
        _projectStateService = projectStateService;
        _dialogService = dialogService;
    }

    public void OnNavigatedTo(object? parameter) => _ = LoadMilestonesAsync();
    public void OnNavigatedFrom() { }

    partial void OnSelectedMilestoneChanged(MilestoneRowViewModel? value)
    {
        OnPropertyChanged(nameof(IsDetailVisible));
        OnPropertyChanged(nameof(IsSelectedMilestoneIncomplete));
        if (value is null)
        {
            LinkedNodes.Clear();
            AllProjectNodes.Clear();
            return;
        }
        EditTitle = value.Milestone.Title;
        EditDueDate = new DateTimeOffset(DateTime.SpecifyKind(value.Milestone.DueDate, DateTimeKind.Utc));
        _ = LoadLinkedNodesAsync(value.Milestone);
        _ = LoadAllProjectNodesAsync();
    }

    [RelayCommand]
    private async Task LoadMilestonesAsync()
    {
        if (_projectStateService.ActiveProject is not { Id: var projectId }) return;
        try
        {
            Milestones.Clear();
            IReadOnlyList<Milestone> milestones = await _milestoneService.GetByProjectAsync(projectId);
            foreach (Milestone m in milestones)
            {
                List<Guid> ids = JsonSerializer.Deserialize<List<Guid>>(m.LinkedNodeIds) ?? [];
                Milestones.Add(new MilestoneRowViewModel
                {
                    Milestone = m,
                    DueDateLabel = m.DueDate.ToString("dd MMM yyyy"),
                    LinkedNodeCount = ids.Count
                });
            }
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Load Error", ex.Message);
        }
    }

    [RelayCommand]
    private async Task AddMilestoneAsync()
    {
        if (_projectStateService.ActiveProject is not { Id: var projectId }) return;
        if (string.IsNullOrWhiteSpace(NewTitle)) return;

        DateTime dueDate = NewDueDate?.UtcDateTime ?? DateTime.UtcNow.Date.AddDays(7);
        try
        {
            await _milestoneService.CreateAsync(projectId, NewTitle, dueDate);
            NewTitle = string.Empty;
            NewDueDate = null;
            await LoadMilestonesAsync();
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Add Milestone Error", ex.Message);
        }
    }

    [RelayCommand]
    private async Task SaveEditAsync()
    {
        if (SelectedMilestone is null || string.IsNullOrWhiteSpace(EditTitle)) return;

        Guid savedId = SelectedMilestone.Milestone.Id;
        SelectedMilestone.Milestone.Title = EditTitle.Trim();
        if (EditDueDate.HasValue)
            SelectedMilestone.Milestone.DueDate = EditDueDate.Value.UtcDateTime;
        try
        {
            await _milestoneService.UpdateAsync(SelectedMilestone.Milestone);
            await LoadMilestonesAsync();
            SelectedMilestone = Milestones.FirstOrDefault(r => r.Milestone.Id == savedId);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Save Error", ex.Message);
        }
    }

    [RelayCommand]
    private async Task MarkCompleteAsync(MilestoneRowViewModel? row)
    {
        if (row is null || row.Milestone.IsComplete) return;

        bool confirmed = await _dialogService.ShowConfirmAsync(
            "Mark Complete",
            $"Mark \"{row.Milestone.Title}\" as complete?");
        if (!confirmed) return;

        try
        {
            await _milestoneService.MarkCompleteAsync(row.Milestone.Id);
            SelectedMilestone = null;
            await LoadMilestonesAsync();
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Mark Complete Error", ex.Message);
        }
    }

    [RelayCommand]
    private async Task DeleteMilestoneAsync(MilestoneRowViewModel? row)
    {
        if (row is null) return;

        bool confirmed = await _dialogService.ShowConfirmAsync(
            "Delete Milestone",
            $"Delete \"{row.Milestone.Title}\"?");
        if (!confirmed) return;

        try
        {
            await _milestoneService.DeleteAsync(row.Milestone.Id);
            if (SelectedMilestone == row) SelectedMilestone = null;
            await LoadMilestonesAsync();
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Delete Error", ex.Message);
        }
    }

    [RelayCommand]
    private async Task AddLinkedNodeAsync()
    {
        if (SelectedMilestone is null || NewLinkedNode is null) return;

        List<Guid> ids = JsonSerializer.Deserialize<List<Guid>>(SelectedMilestone.Milestone.LinkedNodeIds) ?? [];
        if (ids.Contains(NewLinkedNode.Id)) return;

        ids.Add(NewLinkedNode.Id);
        SelectedMilestone.Milestone.LinkedNodeIds = JsonSerializer.Serialize(ids);
        try
        {
            await _milestoneService.UpdateAsync(SelectedMilestone.Milestone);
            NewLinkedNode = null;
            await LoadLinkedNodesAsync(SelectedMilestone.Milestone);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Link Error", ex.Message);
        }
    }

    [RelayCommand]
    private async Task RemoveLinkedNodeAsync(WbsNode? node)
    {
        if (node is null || SelectedMilestone is null) return;

        List<Guid> ids = JsonSerializer.Deserialize<List<Guid>>(SelectedMilestone.Milestone.LinkedNodeIds) ?? [];
        ids.Remove(node.Id);
        SelectedMilestone.Milestone.LinkedNodeIds = JsonSerializer.Serialize(ids);
        try
        {
            await _milestoneService.UpdateAsync(SelectedMilestone.Milestone);
            await LoadLinkedNodesAsync(SelectedMilestone.Milestone);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Unlink Error", ex.Message);
        }
    }

    private async Task LoadLinkedNodesAsync(Milestone milestone)
    {
        LinkedNodes.Clear();
        NewLinkedNode = null;
        try
        {
            List<Guid> ids = JsonSerializer.Deserialize<List<Guid>>(milestone.LinkedNodeIds) ?? [];
            foreach (Guid id in ids)
            {
                WbsNode? node = await _wbsService.GetByIdAsync(id);
                if (node is not null) LinkedNodes.Add(node);
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
            foreach (WbsNode node in nodes)
                AllProjectNodes.Add(node);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Load Error", ex.Message);
        }
    }
}
