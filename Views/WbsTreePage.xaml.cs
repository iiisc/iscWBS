using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using iscWBS.Core.Services;
using iscWBS.ViewModels;

namespace iscWBS.Views;

public sealed partial class WbsTreePage : Page
{
    public WbsTreeViewModel ViewModel { get; }

    public WbsTreePage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<WbsTreeViewModel>();
        ViewModel.ExpandNodeRequested += OnExpandNodeRequested;
    }

    private void OnExpandNodeRequested(object? sender, WbsNodeViewModel nodeVm)
    {
        TreeViewNode? node = FindTreeViewNode(WbsTreeView.RootNodes, nodeVm);
        if (node is not null)
            node.IsExpanded = true;
    }

    private static TreeViewNode? FindTreeViewNode(IList<TreeViewNode> nodes, WbsNodeViewModel target)
    {
        foreach (TreeViewNode node in nodes)
        {
            if (node.Content == target) return node;
            TreeViewNode? found = FindTreeViewNode(node.Children, target);
            if (found is not null) return found;
        }
        return null;
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

    private void WbsTreeView_Expanding(TreeView sender, TreeViewExpandingEventArgs e)
    {
        if (e.Node.Content is WbsNodeViewModel nodeVm)
        {
            nodeVm.IsExpanded = true;
            ViewModel.ExpandNodeCommand.Execute(nodeVm);
        }
    }

    private void WbsTreeView_Collapsed(TreeView sender, TreeViewCollapsedEventArgs e)
    {
        if (e.Node.Content is WbsNodeViewModel nodeVm)
            nodeVm.IsExpanded = false;
    }

    private void WbsTreeView_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs e)
    {
        if (e.InvokedItem is WbsNodeViewModel nodeVm)
            ViewModel.SelectedNode = nodeVm;
    }

    private void WbsTreeView_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement element && element.DataContext is WbsNodeViewModel nodeVm)
        {
            ViewModel.SelectedNode = nodeVm;
            FlyoutBase flyout = FlyoutBase.GetAttachedFlyout(WbsTreeView);
            flyout?.ShowAt(element, new FlyoutShowOptions { Position = e.GetPosition(element) });
        }
    }

    private void TitleTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { Tag: WbsNodeViewModel nodeVm })
            ViewModel.CommitInlineEditCommand.Execute(nodeVm);
    }
}

