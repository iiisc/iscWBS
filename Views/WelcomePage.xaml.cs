using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;
using iscWBS.ViewModels;
using iscWBS.Views.Controls;

namespace iscWBS.Views;

public sealed partial class WelcomePage : Page
{
    public WelcomeViewModel ViewModel { get; }

    public WelcomePage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<WelcomeViewModel>();
    }

    private async void BtnNewProject_Click(object sender, RoutedEventArgs e)
    {
        var control = new NewProjectControl();
        var dialog = new ContentDialog
        {
            Title = "New Project",
            Content = control,
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        ContentDialogResult result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary
            && !string.IsNullOrWhiteSpace(control.ProjectName)
            && !string.IsNullOrWhiteSpace(control.FolderPath))
        {
            await ViewModel.CreateProjectAsync(
                control.ProjectName, control.Owner, control.Currency, control.FolderPath);
        }
    }

    private async void BtnOpenProject_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        picker.FileTypeFilter.Add(".iscwbs");

        StorageFile? file = await picker.PickSingleFileAsync();
        if (file is not null)
            await ViewModel.OpenProjectAsync(file.Path);
    }

    private async void RecentProjectsList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is string filePath)
            await ViewModel.OpenProjectAsync(filePath);
    }
}
