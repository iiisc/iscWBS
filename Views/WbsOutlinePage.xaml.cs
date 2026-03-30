using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
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
}
