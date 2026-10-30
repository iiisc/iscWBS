using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using iscWBS.Helpers;
using iscWBS.ViewModels;
using iscWBS.Views.Controls;

namespace iscWBS.Views;

public sealed partial class DashboardPage : Page, IReloadablePage
{
    public DashboardViewModel ViewModel { get; }

    public DashboardPage()
    {
        InitializeComponent();
        ViewModel = App.Services.CreateDashboardViewModel();
        AllDoneImage.Source = IscEmptyState.LoadIllustration("AllDone");
    }

    public static Thickness Indent(int depth) => new(depth * 16, 0, 0, 0);

    public static string DaysLateText(int days) => Strings.Format("Dashboard_DaysLate", days);

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.OnNavigatedTo();
    }

    public void Reload() => ViewModel.OnNavigatedTo();
}
