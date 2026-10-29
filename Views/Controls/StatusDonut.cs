using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Path = Microsoft.UI.Xaml.Shapes.Path;
using Windows.Foundation;
using iscWBS.Helpers;
using iscWBS.ViewModels;

namespace iscWBS.Views.Controls;

/// <summary>Ring chart of the dashboard's status counts, drawn with plain shapes.</summary>
public sealed partial class StatusDonut : Canvas
{
    private const double Diameter = 200;
    private const double RingWidth = 36;
    private const double Radius = (Diameter - RingWidth) / 2;

    private List<StatusSlice> _slices = [];

    public StatusDonut()
    {
        Width = Diameter;
        Height = Diameter;
    }

    // A plain property is enough: x:Bind assigns it directly, so no dependency property is needed.
    public List<StatusSlice> Slices
    {
        get => _slices;
        set
        {
            _slices = value;
            Draw();
        }
    }

    private void Draw()
    {
        Children.Clear();
        int total = _slices.Sum(s => s.Count);
        if (total == 0) return;

        double start = -90; // 12 o'clock
        foreach (StatusSlice slice in _slices.Where(s => s.Count > 0))
        {
            var brush = new SolidColorBrush(StatusPalette.Color(slice.Status));
            double sweep = 360.0 * slice.Count / total;
            Children.Add(sweep >= 360 ? Ring(brush) : Arc(start, sweep, brush));
            start += sweep;
        }
    }

    // An arc whose end point equals its start point draws nothing, so a single full slice is an ellipse.
    private static Ellipse Ring(Brush brush)
    {
        var ring = new Ellipse { Width = Diameter - RingWidth, Height = Diameter - RingWidth, Stroke = brush, StrokeThickness = RingWidth };
        SetLeft(ring, RingWidth / 2);
        SetTop(ring, RingWidth / 2);
        return ring;
    }

    private static Path Arc(double startDegrees, double sweepDegrees, Brush brush)
    {
        var figure = new PathFigure { StartPoint = PointAt(startDegrees) };
        figure.Segments.Add(new ArcSegment
        {
            Point = PointAt(startDegrees + sweepDegrees),
            Size = new Size(Radius, Radius),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = sweepDegrees > 180
        });
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return new Path { Data = geometry, Stroke = brush, StrokeThickness = RingWidth };
    }

    private static Point PointAt(double degrees)
    {
        double radians = degrees * Math.PI / 180;
        return new Point(Diameter / 2 + Radius * Math.Cos(radians), Diameter / 2 + Radius * Math.Sin(radians));
    }
}
