using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using iscWBS.ViewModels;

namespace iscWBS.Views;

public sealed partial class GanttPage : Page
{
    public GanttViewModel ViewModel { get; }

    public GanttPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<GanttViewModel>();
    }
}
