using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using iscWBS.Helpers;
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
        if (await NewProjectControl.ShowAsync(XamlRoot) is { } project)
            await ViewModel.CreateProjectAsync(project.ProjectName, project.FilePath, project.Owner);
    }

    private async void BtnOpenProject_Click(object sender, RoutedEventArgs e)
    {
        if (await FilePickers.PickProjectFileAsync() is { } file)
            await ViewModel.OpenProjectAsync(file.Path);
    }

    private async void RecentProjectsList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is string filePath)
            await ViewModel.OpenProjectAsync(filePath);
    }
}
