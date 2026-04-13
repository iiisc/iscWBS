using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using iscWBS.Core.Services;
using iscWBS.ViewModels;

namespace iscWBS.Views;

public sealed partial class ShellWindow : Window
{
    private readonly INavigationService _navigationService;
    private readonly IDialogService _dialogService;
    private readonly ISettingsService _settingsService;
    private readonly IProjectStateService _projectStateService;

    public ShellViewModel ViewModel { get; }

    public ShellWindow()
    {
        InitializeComponent();

        ViewModel = App.Services.GetRequiredService<ShellViewModel>();
        _navigationService = App.Services.GetRequiredService<INavigationService>();
        _dialogService = App.Services.GetRequiredService<IDialogService>();
        _settingsService = App.Services.GetRequiredService<ISettingsService>();
        _projectStateService = App.Services.GetRequiredService<IProjectStateService>();

        AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;

        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShellViewModel.WindowTitle))
                AppWindow.Title = ViewModel.WindowTitle;
        };

        AppWindow.Title = ViewModel.WindowTitle;

        RootGrid.Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _dialogService.Initialize(RootGrid.XamlRoot);
        _navigationService.Initialize(ContentFrame);

        IReadOnlyList<string> recent = _settingsService.GetRecentProjects();
        string? mostRecent = recent.Count > 0 ? recent[0] : null;

        if (mostRecent is not null && File.Exists(mostRecent))
        {
            try
            {
                await _projectStateService.OpenProjectAsync(mostRecent);
                return;
            }
            catch (Exception ex)
            {
                _settingsService.RemoveRecentProject(mostRecent);
                Helpers.Logger.Write(ex);
                await _dialogService.ShowErrorAsync("Cannot Open Project", ex.Message);
            }
        }
        else if (mostRecent is not null)
        {
            _settingsService.RemoveRecentProject(mostRecent);
        }

        _navigationService.NavigateTo("WelcomePage");
    }

    private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer?.Tag is not string tag)
            return;

        try
        {
            _navigationService.NavigateTo(tag);
        }
        catch (Exception ex)
        {
            Helpers.Logger.Write(ex);
            if (System.Diagnostics.Debugger.IsAttached)
                System.Diagnostics.Debugger.Break();
            _ = _dialogService.ShowErrorAsync("Navigation Error", ex.Message);
        }
    }
}

