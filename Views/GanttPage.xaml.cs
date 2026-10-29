using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using iscWBS.Core.Models;
using iscWBS.Helpers;
using iscWBS.ViewModels;
using Color = Windows.UI.Color;

namespace iscWBS.Views;

public sealed partial class GanttPage : Page
{
    private const double _rowHeight    = GanttLayout.RowHeight;
    private const double _barHeight    = GanttLayout.BarHeight;
    private const double _barOffsetY   = (_rowHeight - _barHeight) / 2.0;
    private const double _headerHeight = GanttLayout.HeaderHeight;
    private const double _headerTierH  = _headerHeight / 2.0;
    private const double _labelWidth   = 220.0; // matches the label column width in GanttPage.xaml

    private readonly SolidColorBrush _gridLineBrush    = new(Color.FromArgb(0x20, 0x80, 0x80, 0x80));
    private readonly SolidColorBrush _majorLineBrush   = new(Color.FromArgb(0x40, 0x80, 0x80, 0x80));
    private readonly SolidColorBrush _progressBrush    = new(Color.FromArgb(0x45, 0xFF, 0xFF, 0xFF));
    private readonly SolidColorBrush _barTextBrush     = new(Colors.White);
    private readonly SolidColorBrush _noBarDotBrush    = new(Color.FromArgb(0x60, 0x80, 0x80, 0x80));
    private readonly SolidColorBrush _parentAccentBrush = new(Color.FromArgb(0x90, 0x00, 0x78, 0xD4));
    private readonly Dictionary<WbsStatus, SolidColorBrush> _statusBrushes =
        Enum.GetValues<WbsStatus>().ToDictionary(s => s, s => new SolidColorBrush(StatusPalette.Color(s)));

    public GanttViewModel ViewModel { get; }

    public GanttPage()
    {
        ViewModel = App.Services.CreateGanttViewModel();
        InitializeComponent();
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GanttViewModel.Layout))
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
            double indent = 12.0 + row.Depth * 14.0;

            if (row.IsParent)
            {
                var accent = new Rectangle
                {
                    Width   = 3,
                    Height  = _rowHeight * 0.5,
                    Fill    = _parentAccentBrush,
                    RadiusX = 1.5,
                    RadiusY = 1.5
                };
                Place(LabelCanvas, accent, indent - 7, row.RowTop + _rowHeight * 0.25);
            }

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
        foreach (GanttRow row in ViewModel.Layout.Rows)
        {
            double barTop = row.RowTop + _barOffsetY;

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
                RadiusX = 3,
                RadiusY = 3
            };
            Place(ChartCanvas, bar, row.BarLeft, barTop);

            if (row.PercentComplete > 0 && row.BarWidth > 0)
            {
                var progress = new Rectangle
                {
                    Width   = Math.Clamp(row.BarWidth * row.PercentComplete, 4, row.BarWidth),
                    Height  = _barHeight,
                    Fill    = _progressBrush,
                    RadiusX = 3,
                    RadiusY = 3
                };
                Place(ChartCanvas, progress, row.BarLeft, barTop);

                if (row.BarWidth > 36)
                {
                    var percent = new TextBlock
                    {
                        Text       = $"{Math.Round(row.PercentComplete * 100)}%",
                        FontSize   = 9,
                        Foreground = _barTextBrush
                    };
                    Place(ChartCanvas, percent, row.BarLeft + 4, barTop + (_barHeight - 12) / 2.0);
                }
            }

            if (!string.IsNullOrEmpty(row.AssignedTo) && row.BarWidth > 30)
            {
                var assignee = new TextBlock { Text = row.AssignedTo, FontSize = 9, Foreground = assigneeFg };
                Place(ChartCanvas, assignee, row.BarLeft, barTop + _barHeight + 1);
            }
        }
    }

    private void DrawMilestones(double rowsH)
    {
        const double size = 12.0;
        var lineBrush     = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xB9, 0x00));
        var openBrush     = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xB9, 0x00));
        var strokeBrush   = new SolidColorBrush(Color.FromArgb(0x80, 0x00, 0x00, 0x00));
        foreach (GanttMilestoneMarker marker in ViewModel.Layout.MilestoneMarkers)
        {
            AddVLine(ChartCanvas, marker.X, 0, rowsH, lineBrush, 1.5);

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
        var startBrush  = new SolidColorBrush(Color.FromArgb(0x80, 0x80, 0x80, 0x80));
        var finishBrush = new SolidColorBrush(Color.FromArgb(0xC0, 0xCA, 0x50, 0x10));
        const double exitGap   = 8.0;
        const double arrowSize = 5.0;

        foreach (GanttDependencyArrow arrow in ViewModel.Layout.DependencyArrows)
        {
            bool   isFinish  = DependencyConstraints.IsFinishConstraint(arrow.Type);
            var    brush     = isFinish ? finishBrush : startBrush;
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
        double todayX = ViewModel.Layout.TodayX;
        if (todayX < 0) return;

        var todayBrush = new SolidColorBrush(Color.FromArgb(0xEE, 0xC5, 0x0F, 0x1F));
        AddVLine(HeaderCanvas, todayX, 0, _headerHeight, todayBrush, 2);

        var todayLabel = new TextBlock
        {
            Text       = Strings.Get("Gantt_Today"),
            FontSize   = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Resources["GanttTodayForeground"]
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

    private static void AddVLine(Canvas canvas, double x, double y1, double y2,
        SolidColorBrush brush, double thickness)
        => canvas.Children.Add(new Line { X1 = x, Y1 = y1, X2 = x, Y2 = y2, Stroke = brush, StrokeThickness = thickness });
}
