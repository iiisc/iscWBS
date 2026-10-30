using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iscWBS.Core.Models;
using iscWBS.Core.Services;
using iscWBS.Helpers;

namespace iscWBS.ViewModels;

public sealed partial class RisksViewModel : ObservableObject
{
    private readonly RiskService _riskService;
    private readonly ProjectStateService _projectStateService;
    private readonly DialogService _dialogService;

    public ObservableCollection<RiskRowViewModel> Risks { get; } = new();

    public List<RiskLevelOption> LevelOptions { get; } =
        Enum.GetValues<RiskLevel>().Select(level => new RiskLevelOption(level, level.ToDisplayString())).ToList();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailVisible))]
    [NotifyCanExecuteChangedFor(nameof(SaveEditCommand))]
    public partial RiskRowViewModel? SelectedRisk { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddRiskCommand))]
    public partial string NewTitle { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveEditCommand))]
    public partial string EditTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EditDescription { get; set; } = string.Empty;

    [ObservableProperty]
    public partial RiskLevelOption? EditLikelihood { get; set; }

    [ObservableProperty]
    public partial RiskLevelOption? EditImpact { get; set; }

    [ObservableProperty]
    public partial string EditOwner { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EditResponse { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool EditIsClosed { get; set; }

    [ObservableProperty]
    public partial bool HasNoRisks { get; private set; }

    public bool IsDetailVisible => SelectedRisk is not null;

    public RisksViewModel(RiskService riskService, ProjectStateService projectStateService, DialogService dialogService)
    {
        _riskService = riskService;
        _projectStateService = projectStateService;
        _dialogService = dialogService;
    }

    public void OnNavigatedTo() => _ = LoadAsync(selectId: null);

    partial void OnSelectedRiskChanged(RiskRowViewModel? value)
    {
        if (value is null) return;
        Risk risk = value.Risk;
        EditTitle = risk.Title;
        EditDescription = risk.Description;
        EditLikelihood = LevelOptions.First(o => o.Level == risk.Likelihood);
        EditImpact = LevelOptions.First(o => o.Level == risk.Impact);
        EditOwner = risk.Owner;
        EditResponse = risk.Response;
        EditIsClosed = risk.IsClosed;
    }

    private async Task LoadAsync(Guid? selectId)
    {
        if (_projectStateService.ActiveProject is not { Id: var projectId }) return;
        try
        {
            Risks.Clear();
            foreach (Risk risk in await _riskService.GetByProjectAsync(projectId))
                Risks.Add(new RiskRowViewModel(risk));
            HasNoRisks = Risks.Count == 0;
            SelectedRisk = Risks.FirstOrDefault(r => r.Risk.Id == selectId);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("ErrorTitle_Load", ex);
        }
    }

    private bool CanAddRisk => !string.IsNullOrWhiteSpace(NewTitle);

    [RelayCommand(CanExecute = nameof(CanAddRisk))]
    private async Task AddRiskAsync()
    {
        if (_projectStateService.ActiveProject is not { Id: var projectId }) return;
        try
        {
            Risk risk = await _riskService.CreateAsync(projectId, NewTitle);
            NewTitle = string.Empty;
            await LoadAsync(risk.Id);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("ErrorTitle_AddRisk", ex);
        }
    }

    private bool CanSaveEdit => SelectedRisk is not null && !string.IsNullOrWhiteSpace(EditTitle);

    [RelayCommand(CanExecute = nameof(CanSaveEdit))]
    private async Task SaveEditAsync()
    {
        if (SelectedRisk is null) return;
        var risk = new Risk
        {
            Id = SelectedRisk.Risk.Id,
            ProjectId = SelectedRisk.Risk.ProjectId,
            Title = EditTitle,
            Description = EditDescription,
            Likelihood = EditLikelihood?.Level ?? RiskLevel.Medium,
            Impact = EditImpact?.Level ?? RiskLevel.Medium,
            Owner = EditOwner,
            Response = EditResponse,
            IsClosed = EditIsClosed,
            CreatedAt = SelectedRisk.Risk.CreatedAt
        };
        try
        {
            await _riskService.UpdateAsync(risk);
            await LoadAsync(risk.Id);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("ErrorTitle_Save", ex);
        }
    }

    [RelayCommand]
    private async Task DeleteRiskAsync(RiskRowViewModel? row)
    {
        if (row is null) return;
        if (!await _dialogService.ShowConfirmAsync(Strings.Get("Risks_DeleteTitle"), Strings.Format("Risks_DeleteConfirm", row.Risk.Title)))
            return;
        try
        {
            await _riskService.DeleteAsync(row.Risk.Id);
            await LoadAsync(SelectedRisk == row ? null : SelectedRisk?.Risk.Id);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("ErrorTitle_Delete", ex);
        }
    }
}

public sealed class RiskRowViewModel(Risk risk)
{
    public Risk Risk { get; } = risk;
    public string StatusLabel => Risk.IsClosed ? Strings.Get("Risks_Closed") : Risk.Severity.ToDisplayString();
    public bool IsHigh => Risk.Severity == RiskLevel.High && !Risk.IsClosed;
    public bool IsMedium => Risk.Severity == RiskLevel.Medium && !Risk.IsClosed;
    public bool IsLow => Risk.Severity == RiskLevel.Low && !Risk.IsClosed;
    public string RatingLabel => Strings.Format("Risks_Rating", Risk.Likelihood.ToDisplayString(), Risk.Impact.ToDisplayString());
    public double Opacity => Risk.IsClosed ? 0.6 : 1;

    public override string ToString() => $"{Risk.Title}, {StatusLabel}";
}

public sealed record RiskLevelOption(RiskLevel Level, string Label)
{
    public override string ToString() => Label;
}
