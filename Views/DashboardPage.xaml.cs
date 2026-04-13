using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;
using iscWBS.ViewModels;

namespace iscWBS.Views;

public sealed partial class DashboardPage : Page
{
    public DashboardViewModel ViewModel { get; }

    public DashboardPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<DashboardViewModel>();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.OnNavigatedTo(e.Parameter);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.OnNavigatedFrom();
    }

    private async void ExportPdfButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsExporting) return;

        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.SuggestedFileName = $"Status Report {DateTime.Today:yyyy-MM-dd}";
        picker.FileTypeChoices.Add("PDF Document", new List<string> { ".pdf" });

        StorageFile? file = await picker.PickSaveFileAsync();
        if (file is null) return;

        ExportPdfButton.IsEnabled = false;
        bool success = await ViewModel.ExportPdfAsync(file.Path);
        ExportPdfButton.IsEnabled = true;

        if (success)
            await Windows.System.Launcher.LaunchFileAsync(file);
    }
}

