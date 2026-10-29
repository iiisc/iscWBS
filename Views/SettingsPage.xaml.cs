using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using iscWBS.Helpers;
using iscWBS.ViewModels;
using iscWBS.Views.Controls;

namespace iscWBS.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        InitializeComponent();
        ViewModel = App.Services.CreateSettingsViewModel();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.OnNavigatedTo();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.OnNavigatedFrom();
    }

    private async void BtnNewProject_Click(object sender, RoutedEventArgs e)
    {
        if (await NewProjectControl.ShowAsync(XamlRoot) is { } project)
            await ViewModel.CreateProjectAsync(project.ProjectName, project.FilePath, project.Owner);
    }

    private async void BtnOpenProject_Click(object sender, RoutedEventArgs e)
    {
        if (await FilePickers.PickProjectFileAsync() is { } path)
            await ViewModel.OpenProjectAsync(path);
    }

    private async void RecentProjectsList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is string filePath)
            await ViewModel.OpenProjectAsync(filePath);
    }

    private void RemoveRecentProject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string filePath })
            ViewModel.RemoveRecentProjectCommand.Execute(filePath);
    }
}
