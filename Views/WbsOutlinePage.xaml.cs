using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using iscWBS.ViewModels;

namespace iscWBS.Views;

public sealed partial class WbsOutlinePage : Page, IReloadablePage
{
    public WbsOutlineViewModel ViewModel { get; }

    public WbsOutlinePage()
    {
        InitializeComponent();
        ViewModel = App.Services.CreateWbsOutlineViewModel();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.OnNavigatedTo();
    }

    private void OutlineListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is WbsOutlineRowViewModel row)
            ViewModel.SelectRowCommand.Execute(row);
    }

    public void Reload() => ViewModel.OnNavigatedTo();
}
