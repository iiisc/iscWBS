using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using iscWBS.Core.Models;
using iscWBS.Helpers;

namespace iscWBS.Views.Controls;

/// <summary>The brand's icon for a status in the status colour, so status never rests on colour alone.</summary>
public sealed partial class StatusMarker : UserControl
{
    // Typed as object: WinRT has no metadata for the app's own enum.
    public static readonly DependencyProperty StatusProperty =
        DependencyProperty.Register(
            nameof(Status),
            typeof(object),
            typeof(StatusMarker),
            new PropertyMetadata(WbsStatus.NotStarted, (d, _) => ((StatusMarker)d).ApplyStatus()));

    public WbsStatus Status
    {
        get => (WbsStatus)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public StatusMarker()
    {
        InitializeComponent();
        ApplyStatus();
    }

    private void ApplyStatus()
    {
        IscIcon.SetKind(Icon, Status switch
        {
            WbsStatus.InProgress => "StatusInProgress",
            WbsStatus.Complete   => "StatusDone",
            WbsStatus.Blocked    => "StatusBlocked",
            _                    => "StatusTodo",
        });
        Icon.Foreground = (Brush)Resources[Status.ToString()];
    }
}
