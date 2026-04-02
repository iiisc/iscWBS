using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using iscWBS.Core.Models;
using iscWBS.ViewModels;

namespace iscWBS.Views;

public sealed partial class MilestonesPage : Page
{
    public MilestonesViewModel ViewModel { get; }

    public MilestonesPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<MilestonesViewModel>();
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

    private void DeleteMilestone_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MilestoneRowViewModel row })
            ViewModel.DeleteMilestoneCommand.Execute(row);
    }

    private void UnlinkNode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WbsNode node })
            ViewModel.RemoveLinkedNodeCommand.Execute(node);
    }
}
