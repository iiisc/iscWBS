using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using iscWBS.ViewModels;

namespace iscWBS.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
    }
}
