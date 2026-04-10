using System.ComponentModel;
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
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        WbsDiagram.NodeSelected    += WbsDiagram_NodeSelected;
        WbsDiagram.NodeRightTapped += WbsDiagram_NodeRightTapped;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WbsTreeViewModel.BlockedNodeIds))
            WbsDiagram.BlockedNodeIds = ViewModel.BlockedNodeIds;
        if (e.PropertyName == nameof(WbsTreeViewModel.CompletionBlockedNodeIds))
            WbsDiagram.CompletionBlockedNodeIds = ViewModel.CompletionBlockedNodeIds;
        if (e.PropertyName == nameof(WbsTreeViewModel.ViolatedCompleteNodeIds))
            WbsDiagram.ViolatedCompleteNodeIds = ViewModel.ViolatedCompleteNodeIds;
        if (e.PropertyName == nameof(WbsTreeViewModel.ViolatedInProgressNodeIds))
            WbsDiagram.ViolatedInProgressNodeIds = ViewModel.ViolatedInProgressNodeIds;
        if (e.PropertyName == nameof(WbsTreeViewModel.DiagramDependencies))
            WbsDiagram.Dependencies = ViewModel.DiagramDependencies;
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


