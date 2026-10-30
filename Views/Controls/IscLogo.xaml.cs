using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using iscWBS.Helpers;

namespace iscWBS.Views.Controls;

/// <summary>The app icon with the "iscWBS" wordmark.</summary>
public sealed partial class IscLogo : UserControl
{
    public static readonly DependencyProperty SizeProperty =
        DependencyProperty.Register(
            nameof(Size),
            typeof(double),
            typeof(IscLogo),
            new PropertyMetadata(28d, (d, _) => ((IscLogo)d).ApplySize()));

    /// <summary>Height of the icon; the wordmark scales with it.</summary>
    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public IscLogo()
    {
        InitializeComponent();
        ApplySize();
    }

    private void ApplySize()
    {
        Icon.Width = Icon.Height = Size;
        Icon.Source = EmbeddedAssets.Png("Logo.png", Size);
        Wordmark.FontSize = Size * 0.75;
        Panel.Spacing = Size * 0.3;
    }
}
