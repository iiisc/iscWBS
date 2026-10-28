using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Navigation;
using Windows.Foundation;
using iscWBS.Core.Models;
using iscWBS.ViewModels;

namespace iscWBS.Views;

public sealed partial class WbsTreePage : Microsoft.UI.Xaml.Controls.Page
{
    public WbsTreeViewModel ViewModel { get; }

    public WbsTreePage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<WbsTreeViewModel>();
        WbsDiagram.NodeSelected    += WbsDiagram_NodeSelected;
        WbsDiagram.NodeRightTapped += WbsDiagram_NodeRightTapped;
    }

    private void WbsDiagram_NodeSelected(object? sender, WbsNode e)
        => ViewModel.SelectDiagramNode(e);

    private void CollapseAll_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => WbsDiagram.CollapseAll();
    private void ExpandAll_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)   => WbsDiagram.ExpandAll();

    private void WbsDiagram_NodeRightTapped(object? sender, (WbsNode Node, Point Position) e)
    {
        ViewModel.SelectDiagramNode(e.Node);
        FlyoutBase flyout = FlyoutBase.GetAttachedFlyout(WbsDiagram);
        flyout?.ShowAt(WbsDiagram, new FlyoutShowOptions { Position = e.Position });
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
}


