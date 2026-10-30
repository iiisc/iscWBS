using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using System.Windows.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
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
        // The exe's icon only reaches Explorer; the taskbar and Alt+Tab use the window's own. The build stores the
        // ApplicationIcon in the exe under IDI_APPLICATION, so no separate .ico has to ship.
        AppWindow.SetIcon(Win32Interop.GetIconIdFromIcon(LoadIcon(GetModuleHandle(0), IDI_APPLICATION)));

        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShellViewModel.WindowTitle))
                AppWindow.Title = ViewModel.WindowTitle;
        };

        AppWindow.Title = ViewModel.WindowTitle;

        RootGrid.Loaded += OnLoaded;
        // The app also navigates by itself (opening a project, Outline → Tree), so the highlight follows the page.
        ContentFrame.Navigated += (_, e) => NavView.SelectedItem = NavView.MenuItems.Concat(NavView.FooterMenuItems)
            .OfType<NavigationViewItem>()
            .FirstOrDefault(item => item.Tag as string == e.SourcePageType.Name);
        AddShortcut(VirtualKey.Z, VirtualKeyModifiers.Control, App.Services.Undo.UndoCommand);
        AddShortcut(VirtualKey.Y, VirtualKeyModifiers.Control, App.Services.Undo.RedoCommand);
        AddShortcut(VirtualKey.Z, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, App.Services.Undo.RedoCommand);
        // Text in the WBS detail panel is saved on blur, which closing the window skips.
        Closed += (_, _) =>
        {
            (ContentFrame.Content as IReloadablePage)?.FlushAsync();
            _projectStateService.CloseOnExit();
        };
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

    private void AddShortcut(VirtualKey key, VirtualKeyModifiers modifiers, ICommand command)
    {
        var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
        accelerator.Invoked += (_, args) =>
        {
            // A text box keeps the shortcut for undoing its own typing.
            if (FocusManager.GetFocusedElement(RootGrid.XamlRoot) is TextBox) return;
            args.Handled = true;
            if (command.CanExecute(null))
                command.Execute(null);
        };
        RootGrid.KeyboardAccelerators.Add(accelerator);
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

    private const nint IDI_APPLICATION = 32512;

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW")]
    private static partial nint GetModuleHandle(nint moduleName);

    [LibraryImport("user32.dll", EntryPoint = "LoadIconW")]
    private static partial nint LoadIcon(nint instance, nint iconName);
}

