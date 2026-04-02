using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using iscWBS.ViewModels;

namespace iscWBS.Views;

public sealed partial class ReportsPage : Page
{
    public ReportsViewModel ViewModel { get; }

    public ReportsPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<ReportsViewModel>();
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

