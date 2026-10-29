using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using iscWBS.Core.Services;
using iscWBS.Helpers;
using iscWBS.ViewModels;

namespace iscWBS.Views;

public sealed partial class ShellWindow : Window
{
    private readonly NavigationService _navigationService;
    private readonly DialogService _dialogService;
    private readonly SettingsService _settingsService;
    private readonly ProjectStateService _projectStateService;

    public ShellViewModel ViewModel { get; }

    public ShellWindow()
    {
        InitializeComponent();

        ViewModel = App.Services.CreateShellViewModel();
        _navigationService = App.Services.Navigation;
        _dialogService = App.Services.Dialog;
        _settingsService = App.Services.Settings;
        _projectStateService = App.Services.ProjectState;

        AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        // The exe's icon only reaches Explorer; the taskbar and Alt+Tab use the window's own.
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));

        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShellViewModel.WindowTitle))
                AppWindow.Title = ViewModel.WindowTitle;
        };

        AppWindow.Title = ViewModel.WindowTitle;

        RootGrid.Loaded += OnLoaded;
        // Text in the WBS detail panel is saved on blur, which closing the window skips.
        Closed += (_, _) => (ContentFrame.Content as WbsTreePage)?.ViewModel.SaveIfDirty();
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
                await _dialogService.ShowErrorAsync("ErrorTitle_OpenProject", ex);
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
            if (System.Diagnostics.Debugger.IsAttached)
                System.Diagnostics.Debugger.Break();
            _ = _dialogService.ShowErrorAsync("ErrorTitle_Navigation", ex);
        }
    }
}

