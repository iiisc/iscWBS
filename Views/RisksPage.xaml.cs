using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using iscWBS.ViewModels;

namespace iscWBS.Views;

public sealed partial class RisksPage : Page, IReloadablePage
{
    public RisksViewModel ViewModel { get; }

    public RisksPage()
    {
        InitializeComponent();
        ViewModel = App.Services.CreateRisksViewModel();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.OnNavigatedTo();
    }

    private void DeleteRisk_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RiskRowViewModel row })
            ViewModel.DeleteRiskCommand.Execute(row);
    }

    public void Reload() => ViewModel.OnNavigatedTo();
}
