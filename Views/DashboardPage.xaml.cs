using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using iscWBS.ViewModels;

namespace iscWBS.Views;

public sealed partial class DashboardPage : Page
{
    public DashboardViewModel ViewModel { get; }

    public DashboardPage()
    {
        InitializeComponent();
        ViewModel = App.Services.CreateDashboardViewModel();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.OnNavigatedTo();
    }
}
