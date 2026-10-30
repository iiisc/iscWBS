using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using iscWBS.Core.Models;
using iscWBS.Helpers;
using iscWBS.ViewModels;
using iscWBS.Views.Controls;
using Color = Windows.UI.Color;

namespace iscWBS.Views;

public sealed partial class GanttPage : Page, IReloadablePage
{
    private const double _rowHeight    = GanttLayout.RowHeight;
    private const double _barHeight    = GanttLayout.BarHeight;
    private const double _barOffsetY   = (_rowHeight - _barHeight) / 2.0;
    private const double _headerHeight = GanttLayout.HeaderHeight;
    private const double _headerTierH  = _headerHeight / 2.0;
    private const double _labelWidth   = 220.0; // matches the label column width in GanttPage.xaml

    private const double _barRadius    = 7.0; // the brand's squircle corners, about 31% of the bar height

    private readonly SolidColorBrush _gridLineBrush    = new(Color.FromArgb(0x20, 0x80, 0x80, 0x80));
    private readonly SolidColorBrush _majorLineBrush   = new(Color.FromArgb(0x40, 0x80, 0x80, 0x80));
    private readonly SolidColorBrush _noBarDotBrush    = new(Color.FromArgb(0x60, 0x80, 0x80, 0x80));
    private readonly Dictionary<WbsStatus, SolidColorBrush> _statusBrushes;

    public GanttViewModel ViewModel { get; }

    public GanttPage()
    {
        ViewModel = App.Services.CreateGanttViewModel();
        InitializeComponent();
        _statusBrushes = Enum.GetValues<WbsStatus>().ToDictionary(s => s, s => (SolidColorBrush)Resources[$"Gantt{s}"]);
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(GanttViewModel.Layout) or nameof(GanttViewModel.ShowCriticalPath))
                DrawGantt();
        };
        // Canvas content is drawn with theme-dependent colours, so it must be redrawn on a theme switch.
        ActualThemeChanged += (_, _) => DrawGantt();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.OnNavigatedTo();
        DrawGantt();
    }

    private void GanttScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        LabelScrollViewer.ChangeView(null, GanttScrollViewer.VerticalOffset, null, true);
        HeaderScrollViewer.ChangeView(GanttScrollViewer.HorizontalOffset, null, null, true);
    }

    private void DrawGantt()
    {
        double chartW = ViewModel.Layout.TotalWidth;
        double rowsH  = ViewModel.Layout.TotalHeight;

        LabelHeaderCanvas.Width  = _labelWidth;
        LabelHeaderCanvas.Height = _headerHeight;
        LabelHeaderCanvas.Children.Clear();

        HeaderCanvas.Width  = chartW;
        HeaderCanvas.Height = _headerHeight;
        HeaderCanvas.Children.Clear();

        LabelCanvas.Width  = _labelWidth;
        LabelCanvas.Height = rowsH;
        LabelCanvas.Children.Clear();

        ChartCanvas.Width  = chartW;
        ChartCanvas.Height = rowsH;
        ChartCanvas.Children.Clear();
        _bars.Clear();

        var labelFg     = (Brush)Resources["GanttLabelForeground"];
        var headerBrush = (Brush)Resources["GanttHeaderBackground"];

        DrawLabelHeader(headerBrush, labelFg);
        DrawChartHeader(chartW, headerBrush, labelFg);
        DrawRowBands(chartW);
        DrawVerticalGridLines(rowsH);
        DrawLabels(labelFg);
        DrawBars();
        DrawMilestones(rowsH);
        DrawDependencies();
        DrawTodayLine(rowsH);
    }

    private void DrawLabelHeader(Brush headerBrush, Brush labelFg)
    {
        Place(LabelHeaderCanvas, new Rectangle { Width = _labelWidth, Height = _headerHeight, Fill = headerBrush }, 0, 0);
        AddHLine(LabelHeaderCanvas, 0, _labelWidth, _headerHeight, _majorLineBrush, 1);

        var title = new TextBlock
        {
            Text       = Strings.Get("Gantt_TaskHeader"),
            FontSize   = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = labelFg
        };
        Place(LabelHeaderCanvas, title, 12, (_headerHeight - 16) / 2.0);
    }

    private void DrawChartHeader(double chartW, Brush headerBrush, Brush labelFg)
    {
        var bottomBrush = (Brush)Resources["GanttHeaderBottomBackground"];
        Place(HeaderCanvas, new Rectangle { Width = chartW, Height = _headerTierH, Fill = headerBrush }, 0, 0);
        Place(HeaderCanvas, new Rectangle { Width = chartW, Height = _headerTierH, Fill = bottomBrush }, 0, _headerTierH);

        AddHLine(HeaderCanvas, 0, chartW, _headerTierH, _gridLineBrush, 0.5);
        AddHLine(HeaderCanvas, 0, chartW, _headerHeight, _majorLineBrush, 1);

        foreach (GanttHeaderTick tick in ViewModel.Layout.HeaderTicks)
        {
            bool isMajor = tick.MajorLabel is not null && tick.X > 0;
            AddVLine(HeaderCanvas, tick.X, _headerTierH, _headerHeight, isMajor ? _majorLineBrush : _gridLineBrush, 0.5);
            if (isMajor)
                AddVLine(HeaderCanvas, tick.X, 0, _headerTierH, _majorLineBrush, 1);

            var tickLabel = new TextBlock { Text = tick.Label, FontSize = 10, Foreground = labelFg };
            Place(HeaderCanvas, tickLabel, tick.X + 2, _headerTierH + (_headerTierH - 14) / 2.0);

            if (tick.MajorLabel is not null)
            {
                var majorLabel = new TextBlock
                {
                    Text       = tick.MajorLabel,
                    FontSize   = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = labelFg
                };
                Place(HeaderCanvas, majorLabel, tick.X + 4, (_headerTierH - 14) / 2.0);
            }
        }
    }

    /// <summary>Zebra bands and row separators, drawn on both the label and the chart canvas so they line up.</summary>
    private void DrawRowBands(double chartW)
    {
        var bandBrush = (Brush)Resources["GanttRowBand"];
        IReadOnlyList<GanttRow> rows = ViewModel.Layout.Rows;
        for (int i = 0; i < rows.Count; i++)
        {
            double top = rows[i].RowTop;
            foreach ((Canvas canvas, double width) in new[] { (ChartCanvas, chartW), (LabelCanvas, _labelWidth) })
            {
                if (i % 2 == 0)
                    Place(canvas, new Rectangle { Width = width, Height = _rowHeight, Fill = bandBrush }, 0, top);
                AddHLine(canvas, 0, width, top + _rowHeight, _gridLineBrush, 0.5);
            }
        }
    }

    private void DrawVerticalGridLines(double rowsH)
    {
        foreach (GanttHeaderTick tick in ViewModel.Layout.HeaderTicks)
            AddVLine(ChartCanvas, tick.X, 0, rowsH, _gridLineBrush, 0.5);
    }

    private void DrawLabels(Brush labelFg)
    {
        foreach (GanttRow row in ViewModel.Layout.Rows)
        {
            double indent = 26.0 + row.Depth * 14.0;

            // The branch colour, as in the Outline and the diagram.
            Place(LabelCanvas, new LevelMarker { Level = row.Level, Width = 10, Height = 10 }, indent - 16, row.RowTop + (_rowHeight - 10) / 2.0);

            var label = new TextBlock
            {
                Text              = row.Label,
                Width             = _labelWidth - indent - 8,
                TextTrimming      = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground        = labelFg,
                FontSize          = 12,
                FontWeight        = row.IsParent ? FontWeights.SemiBold : FontWeights.Normal
            };
            Place(LabelCanvas, label, indent, row.RowTop + (_rowHeight - 16) / 2.0);
        }
    }

    private void DrawBars()
    {
        var assigneeFg = (Brush)Resources["GanttAssigneeForeground"];
        var onStatus   = (Brush)Resources["GanttOnStatus"];
        var labelFg    = (Brush)Resources["GanttLabelForeground"];
        foreach (GanttRow row in ViewModel.Layout.Rows)
        {
            double barTop = row.RowTop + _barOffsetY;

            // The saved plan, as a thin line just above where the bar is now.
            if (row.HasBaseline)
            {
                var baseline = new Rectangle
                {
                    Width   = row.BaselineWidth,
                    Height  = 4,
                    Fill    = labelFg,
                    Opacity = 0.45,
                    RadiusX = 2,
                    RadiusY = 2
                };
                Place(ChartCanvas, baseline, row.BaselineLeft, barTop - 6);
            }

            if (!row.HasBar)
            {
                Place(ChartCanvas, new Ellipse { Width = 5, Height = 5, Fill = _noBarDotBrush }, 6, row.RowTop + (_rowHeight - 5) / 2.0);
                continue;
            }

            var bar = new Rectangle
            {
                Width   = row.BarWidth,
                Height  = _barHeight,
                Fill    = _statusBrushes[row.Status],
                RadiusX = _barRadius,
                RadiusY = _barRadius
            };
            if (ViewModel.ShowCriticalPath && row.IsCritical)
            {
                bar.Stroke = labelFg;
                bar.StrokeThickness = 2;
            }
            Place(ChartCanvas, bar, row.BarLeft, barTop);
            var parts = new List<UIElement> { bar };
            _bars[bar] = (row, parts);
            bar.PointerPressed  += Bar_PointerPressed;
            bar.PointerMoved    += Bar_PointerMoved;
            bar.PointerReleased += Bar_PointerReleased;
            bar.PointerExited   += (_, _) => { if (_drag is null) ProtectedCursor = null; };
            bar.PointerCaptureLost += (_, _) => { if (_drag is not null) { _drag = null; DrawGantt(); } };

            // The part still to do is darker: the bar under a veil of the colour its text uses.
            double done = row.BarWidth * row.PercentComplete;
            if (done < row.BarWidth)
            {
                var remaining = new Rectangle
                {
                    Width   = row.BarWidth - done,
                    Height  = _barHeight,
                    Fill    = onStatus,
                    Opacity = 0.3,
                    RadiusX = _barRadius,
                    RadiusY = _barRadius
                };
                Place(ChartCanvas, remaining, row.BarLeft + done, barTop);
                parts.Add(remaining);
            }

            if (row.PercentComplete > 0 && row.BarWidth > 36)
            {
                var percent = new TextBlock
                {
                    Text       = $"{Math.Round(row.PercentComplete * 100)}%",
                    FontSize   = 9,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = onStatus
                };
                Place(ChartCanvas, percent, row.BarLeft + 6, barTop + (_barHeight - 12) / 2.0);
                parts.Add(percent);
            }

            // After the bar rather than under it, where the next row's baseline would cross it.
            if (!string.IsNullOrEmpty(row.AssignedTo))
            {
                var assignee = new TextBlock { Text = row.AssignedTo, FontSize = 10, Foreground = assigneeFg };
                Place(ChartCanvas, assignee, row.BarLeft + row.BarWidth + 6, barTop + (_barHeight - 14) / 2.0);
                parts.Add(assignee);
            }
        }
    }

    // Dragging a bar moves the task; dragging its right end moves only the due date. Both snap to whole days.
    private const double _resizeGrip = 8.0;
    private readonly Dictionary<Rectangle, (GanttRow Row, List<UIElement> Parts)> _bars = [];

    private sealed class BarDrag
    {
        public required Rectangle Bar { get; init; }
        public required double StartX { get; init; }
        public required bool ResizeOnly { get; init; }
        public int Days { get; set; }
    }

    private BarDrag? _drag;

    private bool OnResizeGrip(Rectangle bar, double x) => x >= Canvas.GetLeft(bar) + bar.Width - _resizeGrip;

    private void Bar_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var bar = (Rectangle)sender;
        if (!e.GetCurrentPoint(bar).Properties.IsLeftButtonPressed) return;
        double x = e.GetCurrentPoint(ChartCanvas).Position.X;
        _drag = new BarDrag { Bar = bar, StartX = x, ResizeOnly = OnResizeGrip(bar, x) };
        bar.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void Bar_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var bar = (Rectangle)sender;
        double x = e.GetCurrentPoint(ChartCanvas).Position.X;
        if (_drag is null)
        {
            ProtectedCursor = InputSystemCursor.Create(OnResizeGrip(bar, x) ? InputSystemCursorShape.SizeWestEast : InputSystemCursorShape.SizeAll);
            return;
        }

        double ppd = ViewModel.Layout.PixelsPerDay;
        (GanttRow row, List<UIElement> parts) = _bars[bar];
        _drag.Days = (int)Math.Round((x - _drag.StartX) / ppd);
        double dx = _drag.Days * ppd;
        if (_drag.ResizeOnly)
            bar.Width = Math.Max(4, row.BarWidth + dx);
        else
            foreach (UIElement part in parts)
                part.RenderTransform = new TranslateTransform { X = dx };
    }

    private void Bar_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_drag is not { } drag) return;
        _drag = null;
        drag.Bar.ReleasePointerCapture(e.Pointer);
        Guid nodeId = _bars[drag.Bar].Row.NodeId;
        if (drag.Days != 0)
            _ = ViewModel.ShiftDatesAsync(nodeId, drag.ResizeOnly ? 0 : drag.Days, drag.Days);
    }

    private void DrawMilestones(double rowsH)
    {
        const double size = 12.0;
        var openBrush     = (SolidColorBrush)Resources["GanttMilestone"];
        var strokeBrush   = (SolidColorBrush)Resources["GanttOnStatus"];
        foreach (GanttMilestoneMarker marker in ViewModel.Layout.MilestoneMarkers)
        {
            AddVLine(ChartCanvas, marker.X, 0, rowsH, openBrush, 1.5).Opacity = 0.35;

            // In the sticky header so it stays visible while the rows scroll, and in the top tier, clear of the day numbers.
            var diamond = new Rectangle
            {
                Width           = size,
                Height          = size,
                Fill            = marker.IsComplete ? _statusBrushes[WbsStatus.Complete] : openBrush,
                Stroke          = strokeBrush,
                StrokeThickness = 1,
                RenderTransform = new RotateTransform { Angle = 45, CenterX = size / 2, CenterY = size / 2 }
            };
            Place(HeaderCanvas, diamond, marker.X - size / 2.0, (_headerTierH - size) / 2.0);
            ToolTipService.SetToolTip(diamond, marker.Title);
        }
    }

    private void DrawDependencies()
    {
        var brush = (SolidColorBrush)Resources["GanttConnector"];
        const double exitGap   = 8.0;
        const double arrowSize = 5.0;

        foreach (GanttDependencyArrow arrow in ViewModel.Layout.DependencyArrows)
        {
            bool   isFinish  = DependencyConstraints.IsFinishConstraint(arrow.Type);
            string typeLabel = DisplayText.GetLabel(arrow.Type);

            bool   isFromFinish = arrow.Type is DependencyType.FinishToStart or DependencyType.FinishToFinish;
            double exitX        = isFromFinish ? arrow.FromX + exitGap : arrow.FromX - exitGap;

            var connector = new Polyline
            {
                Stroke          = brush,
                StrokeThickness = 1.5,
                StrokeDashArray = isFinish ? new DoubleCollection { 4, 2 } : null,
                Points          = new PointCollection
                {
                    new(arrow.FromX, arrow.FromY),
                    new(exitX,       arrow.FromY),
                    new(exitX,       arrow.ToY),
                    new(arrow.ToX,   arrow.ToY)
                }
            };
            ToolTipService.SetToolTip(connector, typeLabel);
            ChartCanvas.Children.Add(connector);

            double backX = arrow.ToX >= exitX ? arrow.ToX - arrowSize : arrow.ToX + arrowSize;
            var arrowHead = new Polygon
            {
                Fill   = brush,
                Points = new PointCollection
                {
                    new(arrow.ToX, arrow.ToY),
                    new(backX,     arrow.ToY - arrowSize / 2.0),
                    new(backX,     arrow.ToY + arrowSize / 2.0)
                }
            };
            ChartCanvas.Children.Add(arrowHead);
            if (!string.IsNullOrEmpty(typeLabel))
                ToolTipService.SetToolTip(arrowHead, typeLabel);
        }
    }

    private void DrawTodayLine(double rowsH)
    {
        if (ViewModel.Layout.TodayX is not { } todayX) return;

        var todayBrush = (SolidColorBrush)Resources["GanttToday"];
        AddVLine(HeaderCanvas, todayX, 0, _headerHeight, todayBrush, 2);

        var todayLabel = new TextBlock
        {
            Text       = Strings.Get("Gantt_Today"),
            FontSize   = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = todayBrush
        };
        // In the top tier, which only holds the month names; the bottom tier's day numbers would collide.
        Place(HeaderCanvas, todayLabel, todayX + 3, 4);

        AddVLine(ChartCanvas, todayX, 0, rowsH, todayBrush, 2);
    }

    private static void Place(Canvas canvas, UIElement element, double x, double y)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        canvas.Children.Add(element);
    }

    private static void AddHLine(Canvas canvas, double x1, double x2, double y,
        SolidColorBrush brush, double thickness)
        => canvas.Children.Add(new Line { X1 = x1, Y1 = y, X2 = x2, Y2 = y, Stroke = brush, StrokeThickness = thickness });

    private static Line AddVLine(Canvas canvas, double x, double y1, double y2,
        SolidColorBrush brush, double thickness)
    {
        var line = new Line { X1 = x, Y1 = y1, X2 = x, Y2 = y2, Stroke = brush, StrokeThickness = thickness };
        canvas.Children.Add(line);
        return line;
    }

    public void Reload() => ViewModel.OnNavigatedTo();
}
