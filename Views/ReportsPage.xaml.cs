using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
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
}
