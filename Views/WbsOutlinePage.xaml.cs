using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using iscWBS.ViewModels;

namespace iscWBS.Views;

public sealed partial class WbsOutlinePage : Page
{
    public WbsOutlineViewModel ViewModel { get; }

    public WbsOutlinePage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<WbsOutlineViewModel>();
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

    private void OutlineListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is WbsOutlineRowViewModel row)
            ViewModel.SelectRowCommand.Execute(row);
    }
}

