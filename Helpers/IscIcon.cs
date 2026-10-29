using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace iscWBS.Helpers;

/// <summary>
/// Gives a <see cref="PathIcon"/> a brand icon from Themes\IscIcons.xaml: <c>helpers:IscIcon.Kind="Milestone"</c>.
/// Each icon parses its own Geometry, since WinUI draws a shared one in only one place.
/// </summary>
public static class IscIcon
{
    public static readonly DependencyProperty KindProperty =
        DependencyProperty.RegisterAttached(
            "Kind",
            typeof(string),
            typeof(IscIcon),
            new PropertyMetadata(null, OnKindChanged));

    public static string GetKind(PathIcon icon) => (string)icon.GetValue(KindProperty);

    public static void SetKind(PathIcon icon, string value) => icon.SetValue(KindProperty, value);

    private static void OnKindChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((PathIcon)d).Data = (Geometry)XamlBindingHelper.ConvertValue(
            typeof(Geometry), Application.Current.Resources[$"IscIcon{e.NewValue}"]);
}
