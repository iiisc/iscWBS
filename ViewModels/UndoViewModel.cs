using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iscWBS.Core.Services;
using iscWBS.Views;

namespace iscWBS.ViewModels;

/// <summary>Undo and redo for whichever page is showing; one instance serves the whole app.</summary>
public sealed partial class UndoViewModel : ObservableObject
{
    private readonly ProjectStateService _projectStateService;
    private readonly NavigationService _navigationService;
    private readonly DialogService _dialogService;

    public UndoViewModel(ProjectStateService projectStateService, NavigationService navigationService, DialogService dialogService)
    {
        _projectStateService = projectStateService;
        _navigationService = navigationService;
        _dialogService = dialogService;
        projectStateService.History.Changed += (_, _) =>
        {
            UndoCommand.NotifyCanExecuteChanged();
            RedoCommand.NotifyCanExecuteChanged();
        };
    }

    private ProjectHistory History => _projectStateService.History;

    private bool CanUndo => History.CanUndo;
    private bool CanRedo => History.CanRedo;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private Task UndoAsync() => StepAsync(History.UndoAsync);

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private Task RedoAsync() => StepAsync(History.RedoAsync);

    private async Task StepAsync(Func<Task<bool>> step)
    {
        var page = _navigationService.CurrentPage as IReloadablePage;
        // An edit still in a text box is saved first, so it is the step that gets undone rather than lost.
        if (page is not null)
            await page.FlushAsync();
        try
        {
            if (!await step()) return;
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("ErrorTitle_Undo", ex);
            return;
        }
        page?.Reload();
    }
}
