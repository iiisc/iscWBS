using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI.ViewManagement;
using iscWBS.Helpers;
using iscWBS.ViewModels;

namespace iscWBS.Views;

public sealed partial class ReportsPage : Page
{
    public ReportsViewModel ViewModel { get; }

    public ReportsPage()
    {
        InitializeComponent();
        ViewModel = App.Services.CreateReportsViewModel();
        ApplyTheme();
        // Turning High Contrast on or off changes the theme too.
        ActualThemeChanged += (_, _) => ApplyTheme();
    }

    // The banner's colours are drawn into it, so each theme has its own, and High Contrast, which replaces every
    // colour, gets the plain title instead.
    private void ApplyTheme()
    {
        bool highContrast = new AccessibilitySettings().HighContrast;
        Banner.Visibility = highContrast ? Visibility.Collapsed : Visibility.Visible;
        PlainHeader.Visibility = highContrast ? Visibility.Visible : Visibility.Collapsed;

        BannerImage.Source = EmbeddedAssets.Svg(ActualTheme == ElementTheme.Light ? "ReportsHeaderLight.svg" : "ReportsHeader.svg");
    }
}
