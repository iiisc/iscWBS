using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using iscWBS.ViewModels;

namespace iscWBS.Views;

public sealed partial class WbsTreePage : Page
{
    public WbsTreeViewModel ViewModel { get; }

    public WbsTreePage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<WbsTreeViewModel>();
    }
}
