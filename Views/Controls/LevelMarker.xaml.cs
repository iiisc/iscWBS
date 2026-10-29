using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace iscWBS.Views.Controls;

/// <summary>A squircle in the brand colour of a node's branch (see <see cref="Core.Models.WbsHierarchy.BranchLevels"/>).</summary>
public sealed partial class LevelMarker : UserControl
{
    public static readonly DependencyProperty LevelProperty =
        DependencyProperty.Register(
            nameof(Level),
            typeof(int),
            typeof(LevelMarker),
            new PropertyMetadata(1, (d, _) => ((LevelMarker)d).ApplyLevel()));

    /// <summary>1–6.</summary>
    public int Level
    {
        get => (int)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    public LevelMarker()
    {
        InitializeComponent();
        ApplyLevel();
        // The brand's squircle: corners at about 31% of the height.
        SizeChanged += (_, e) => Marker.CornerRadius = new CornerRadius(e.NewSize.Height * 0.31);
    }

    private void ApplyLevel() => Marker.Background = (Brush)Resources[$"Level{Math.Clamp(Level, 1, 6)}"];
}
