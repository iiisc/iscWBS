using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI.ViewManagement;
using iscWBS.ViewModels;
using iscWBS.Views.Controls;

namespace iscWBS.Views;

public sealed partial class ReportsPage : Page
{
    public ReportsViewModel ViewModel { get; }

    public ReportsPage()
    {
        InitializeComponent();
        ViewModel = App.Services.CreateReportsViewModel();
        BannerImage.Source = IscEmptyState.LoadIllustration("ReportsHeader");
        ShowBannerUnlessHighContrast();
        // Turning High Contrast on or off changes the theme.
        ActualThemeChanged += (_, _) => ShowBannerUnlessHighContrast();
    }

    // High Contrast replaces every colour but the artwork's, so it gets the plain title instead.
    private void ShowBannerUnlessHighContrast()
    {
        bool highContrast = new AccessibilitySettings().HighContrast;
        Banner.Visibility = highContrast ? Visibility.Collapsed : Visibility.Visible;
        PlainHeader.Visibility = highContrast ? Visibility.Visible : Visibility.Collapsed;
    }
}
