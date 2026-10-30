using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.Foundation;
using iscWBS.Core.Models;
using iscWBS.ViewModels;

namespace iscWBS.Views;

public sealed partial class WbsTreePage : Microsoft.UI.Xaml.Controls.Page, IReloadablePage
{
    public WbsTreeViewModel ViewModel { get; }

    public WbsTreePage()
    {
        InitializeComponent();
        ViewModel = App.Services.CreateWbsTreeViewModel();
        WbsDiagram.NodeSelected    += WbsDiagram_NodeSelected;
        WbsDiagram.NodeRightTapped += WbsDiagram_NodeRightTapped;
        WbsDiagram.NodeDropped     += (_, e) => _ = ViewModel.MoveIntoAsync(e.NodeId, e.NewParentId);
    }

    public Task FlushAsync() => ViewModel.FlushAsync();

    public void Reload() => ViewModel.OnNavigatedTo(ViewModel.SelectedNode?.Id);

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            ViewModel.Search(sender.Text);
    }

    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if ((args.ChosenSuggestion as WbsNode ?? ViewModel.SearchResults.FirstOrDefault()) is { } node)
        {
            ViewModel.SelectDiagramNode(node);
            WbsDiagram.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
        }
    }

    private void FocusSearch_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        SearchBox.Focus(Microsoft.UI.Xaml.FocusState.Keyboard);
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


