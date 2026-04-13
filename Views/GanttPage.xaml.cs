using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using iscWBS.Core.Models;
using iscWBS.ViewModels;

namespace iscWBS.Views;

public sealed partial class GanttPage : Page
{
    // ─── Status colours ───────────────────────────────────────────────────────
    private static readonly Windows.UI.Color _colorNotStarted = Windows.UI.Color.FromArgb(0xFF, 0x8A, 0x8A, 0x8A);
    private static readonly Windows.UI.Color _colorInProgress  = Windows.UI.Color.FromArgb(0xFF, 0x00, 0x78, 0xD4);
    private static readonly Windows.UI.Color _colorComplete    = Windows.UI.Color.FromArgb(0xFF, 0x10, 0x7C, 0x10);
    private static readonly Windows.UI.Color _colorBlocked     = Windows.UI.Color.FromArgb(0xFF, 0xC5, 0x0F, 0x1F);

    // ─── Grid line colours ─────────────────────────────────────────────────────
    private static readonly Windows.UI.Color _colorGridLine  = Windows.UI.Color.FromArgb(0x20, 0x80, 0x80, 0x80);
    private static readonly Windows.UI.Color _colorMajorLine = Windows.UI.Color.FromArgb(0x40, 0x80, 0x80, 0x80);

    // ─── Layout constants ──────────────────────────────────────────────────────
    private const double _rowHeight      = GanttViewModel.RowHeight;
    private const double _barHeight      = 24.0;
    private const double _barOffsetY     = (_rowHeight - _barHeight) / 2.0;
    private const double _headerHeight   = GanttViewModel.HeaderHeight;
    private const double _headerTopH     = _headerHeight / 2.0;
    private const double _headerBottomH  = _headerHeight / 2.0;

    public GanttViewModel ViewModel { get; }

    private bool _handlersRegistered;

    public GanttPage()
    {
        ViewModel = App.Services.GetRequiredService<GanttViewModel>();
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.OnNavigatedTo(e.Parameter);

        if (!_handlersRegistered)
        {
            ViewModel.Rows.CollectionChanged += (_, _) => DrawGantt();
            ViewModel.PropertyChanged += (_, pe) =>
            {
                if (pe.PropertyName is
                        nameof(GanttViewModel.TotalWidth) or
                        nameof(GanttViewModel.TotalHeight) or
                        nameof(GanttViewModel.HeaderTicks))
                    DrawGantt();
            };
            _handlersRegistered = true;
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.OnNavigatedFrom();
    }

    private void ZoomCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => ViewModel.SelectedZoomIndex = ZoomCombo.SelectedIndex;

    private void GanttScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        LabelScrollViewer.ChangeView(null, GanttScrollViewer.VerticalOffset, null, true);
        HeaderScrollViewer.ChangeView(GanttScrollViewer.HorizontalOffset, null, null, true);
    }

    // ─── Main draw entry point ─────────────────────────────────────────────────

    private void DrawGantt()
    {
        double chartW = ViewModel.TotalWidth;
        double rowsH  = Math.Max(0, ViewModel.TotalHeight - _headerHeight);

        LabelHeaderCanvas.Width  = GanttViewModel.LabelWidth;
        LabelHeaderCanvas.Height = _headerHeight;
        LabelHeaderCanvas.Children.Clear();

        HeaderCanvas.Width  = chartW;
        HeaderCanvas.Height = _headerHeight;
        HeaderCanvas.Children.Clear();

        LabelCanvas.Width  = GanttViewModel.LabelWidth;
        LabelCanvas.Height = rowsH;
        LabelCanvas.Children.Clear();

        ChartCanvas.Width  = chartW;
        ChartCanvas.Height = rowsH;
        ChartCanvas.Children.Clear();

        bool isDark  = ActualTheme == ElementTheme.Dark;
        var  labelFg = new SolidColorBrush(isDark ? Colors.White : Colors.Black);

        DrawLabelHeader(isDark, labelFg);
        DrawChartHeader(chartW, isDark, labelFg);
        DrawRowBands(chartW, isDark);
        DrawVerticalGridLines(rowsH);
        DrawLabels(labelFg, isDark);
        DrawBars();
        DrawMilestones(rowsH);
        DrawDependencies();
        DrawTodayLine(rowsH);
    }

    // ─── Label column header ───────────────────────────────────────────────────

    private void DrawLabelHeader(bool isDark, SolidColorBrush labelFg)
    {
        var bg = new Rectangle
        {
            Width  = GanttViewModel.LabelWidth,
            Height = _headerHeight,
            Fill   = new SolidColorBrush(isDark
                ? Windows.UI.Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF)
                : Windows.UI.Color.FromArgb(0x14, 0x00, 0x00, 0x00))
        };
        Canvas.SetLeft(bg, 0);
        Canvas.SetTop(bg, 0);
        LabelHeaderCanvas.Children.Add(bg);

        AddHLine(LabelHeaderCanvas, 0, GanttViewModel.LabelWidth, _headerHeight,
            new SolidColorBrush(_colorMajorLine), 1);

        var title = new TextBlock
        {
            Text       = "Task",
            FontSize   = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = labelFg
        };
        Canvas.SetLeft(title, 12);
        Canvas.SetTop(title, (_headerHeight - 16) / 2.0);
        LabelHeaderCanvas.Children.Add(title);
    }

    // ─── Chart header (two-tier: major period + minor ticks) ──────────────────

    private void DrawChartHeader(double chartW, bool isDark, SolidColorBrush labelFg)
    {
        var topBg = new Rectangle
        {
            Width  = chartW,
            Height = _headerTopH,
            Fill   = new SolidColorBrush(isDark
                ? Windows.UI.Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF)
                : Windows.UI.Color.FromArgb(0x14, 0x00, 0x00, 0x00))
        };
        Canvas.SetLeft(topBg, 0);
        Canvas.SetTop(topBg, 0);
        HeaderCanvas.Children.Add(topBg);

        var bottomBg = new Rectangle
        {
            Width  = chartW,
            Height = _headerBottomH,
            Fill   = new SolidColorBrush(isDark
                ? Windows.UI.Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)
                : Windows.UI.Color.FromArgb(0x0A, 0x00, 0x00, 0x00))
        };
        Canvas.SetLeft(bottomBg, 0);
        Canvas.SetTop(bottomBg, _headerTopH);
        HeaderCanvas.Children.Add(bottomBg);

        AddHLine(HeaderCanvas, 0, chartW, _headerTopH, new SolidColorBrush(_colorGridLine), 0.5);
        AddHLine(HeaderCanvas, 0, chartW, _headerHeight, new SolidColorBrush(_colorMajorLine), 1);

        DrawHeaderTicks(chartW, labelFg);
    }

    private void DrawHeaderTicks(double chartW, SolidColorBrush labelFg)
    {
        IReadOnlyList<GanttHeaderTick>? ticks = ViewModel.HeaderTicks;
        if (ticks is null or { Count: 0 }) return;

        var gridLine  = new SolidColorBrush(_colorGridLine);
        var majorLine = new SolidColorBrush(_colorMajorLine);

        foreach (GanttHeaderTick tick in ticks)
        {
            if (tick.X < 0) continue;

            // Minor tick line in bottom tier
            AddVLine(HeaderCanvas, tick.X, _headerTopH, _headerHeight,
                tick.IsMajorBoundary && tick.X > 0 ? majorLine : gridLine, 0.5);

            // Major boundary divider through full header
            if (tick.IsMajorBoundary && tick.X > 0)
                AddVLine(HeaderCanvas, tick.X, 0, _headerTopH, majorLine, 1);

            // Minor tick label (bottom tier)
            var tickLabel = new TextBlock
            {
                Text       = tick.Label,
                FontSize   = 10,
                Foreground = labelFg
            };
            Canvas.SetLeft(tickLabel, tick.X + 2);
            Canvas.SetTop(tickLabel, _headerTopH + (_headerBottomH - 14) / 2.0);
            HeaderCanvas.Children.Add(tickLabel);

            // Major period label (top tier)
            if (tick.MajorLabel is not null)
            {
                var majorLabel = new TextBlock
                {
                    Text       = tick.MajorLabel,
                    FontSize   = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = labelFg
                };
                Canvas.SetLeft(majorLabel, tick.X + 4);
                Canvas.SetTop(majorLabel, (_headerTopH - 14) / 2.0);
                HeaderCanvas.Children.Add(majorLabel);
            }
        }
    }

    // ─── Row bands ────────────────────────────────────────────────────────────

    private void DrawRowBands(double chartW, bool isDark)
    {
        var bandBrush = new SolidColorBrush(isDark
            ? Windows.UI.Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF)
            : Windows.UI.Color.FromArgb(0x0A, 0x00, 0x00, 0x00));
        var lineBrush = new SolidColorBrush(_colorGridLine);
        int i = 0;
        foreach (GanttRow row in ViewModel.Rows)
        {
            double top = row.RowTop - _headerHeight;
            if (i % 2 == 0)
            {
                var band = new Rectangle
                {
                    Width  = chartW,
                    Height = _rowHeight,
                    Fill   = bandBrush
                };
                Canvas.SetLeft(band, 0);
                Canvas.SetTop(band, top);
                ChartCanvas.Children.Add(band);
            }
            AddHLine(ChartCanvas, 0, chartW, top + _rowHeight, lineBrush, 0.5);
            i++;
        }
    }

    // ─── Vertical grid lines ──────────────────────────────────────────────────

    private void DrawVerticalGridLines(double rowsH)
    {
        var gridBrush = new SolidColorBrush(_colorGridLine);
        foreach (GanttHeaderTick tick in ViewModel.HeaderTicks ?? [])
            AddVLine(ChartCanvas, tick.X, 0, rowsH, gridBrush, 0.5);
    }

    // ─── Labels (drawn on LabelCanvas) ────────────────────────────────────────

    private void DrawLabels(SolidColorBrush labelFg, bool isDark)
    {
        var bandBrush = new SolidColorBrush(isDark
            ? Windows.UI.Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF)
            : Windows.UI.Color.FromArgb(0x0A, 0x00, 0x00, 0x00));
        var lineBrush = new SolidColorBrush(_colorGridLine);
        int i = 0;
        foreach (GanttRow row in ViewModel.Rows)
        {
            double top = row.RowTop - _headerHeight;

            if (i % 2 == 0)
            {
                var band = new Rectangle
                {
                    Width  = GanttViewModel.LabelWidth,
                    Height = _rowHeight,
                    Fill   = bandBrush
                };
                Canvas.SetLeft(band, 0);
                Canvas.SetTop(band, top);
                LabelCanvas.Children.Add(band);
            }
            AddHLine(LabelCanvas, 0, GanttViewModel.LabelWidth, top + _rowHeight, lineBrush, 0.5);

            double indent = 12.0 + row.Depth * 14.0;

            if (row.IsParent)
            {
                var accent = new Rectangle
                {
                    Width   = 3,
                    Height  = _rowHeight * 0.5,
                    Fill    = new SolidColorBrush(Windows.UI.Color.FromArgb(0x90, 0x00, 0x78, 0xD4)),
                    RadiusX = 1.5,
                    RadiusY = 1.5
                };
                Canvas.SetLeft(accent, indent - 7);
                Canvas.SetTop(accent, top + _rowHeight * 0.25);
                LabelCanvas.Children.Add(accent);
            }

            var label = new TextBlock
            {
                Text              = row.Label,
                Width             = GanttViewModel.LabelWidth - indent - 8,
                TextTrimming      = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground        = labelFg,
                FontSize          = 12,
                FontWeight        = row.IsParent ? FontWeights.SemiBold : FontWeights.Normal
            };
            Canvas.SetLeft(label, indent);
            Canvas.SetTop(label, top + (_rowHeight - 16) / 2.0);
            LabelCanvas.Children.Add(label);

            i++;
        }
    }

    // ─── Task bars ────────────────────────────────────────────────────────────

    private void DrawBars()
    {
        foreach (GanttRow row in ViewModel.Rows)
        {
            double top = row.RowTop - _headerHeight;

            if (row.HasBar)
            {
                Windows.UI.Color barColor = row.Status switch
                {
                    WbsStatus.InProgress => _colorInProgress,
                    WbsStatus.Complete   => _colorComplete,
                    WbsStatus.Blocked    => _colorBlocked,
                    _                    => _colorNotStarted
                };

                var bar = new Rectangle
                {
                    Width   = row.BarWidth,
                    Height  = _barHeight,
                    Fill    = new SolidColorBrush(barColor),
                    RadiusX = 3,
                    RadiusY = 3
                };
                Canvas.SetLeft(bar, row.BarLeft);
                Canvas.SetTop(bar, top + _barOffsetY);
                ChartCanvas.Children.Add(bar);

                if (row.PercentComplete > 0 && row.BarWidth > 0)
                {
                    double fillW = Math.Clamp(row.BarWidth * row.PercentComplete, 4, row.BarWidth);
                    var progress = new Rectangle
                    {
                        Width   = fillW,
                        Height  = _barHeight,
                        Fill    = new SolidColorBrush(Windows.UI.Color.FromArgb(0x45, 0xFF, 0xFF, 0xFF)),
                        RadiusX = 3,
                        RadiusY = 3
                    };
                    Canvas.SetLeft(progress, row.BarLeft);
                    Canvas.SetTop(progress, top + _barOffsetY);
                    ChartCanvas.Children.Add(progress);

                    if (row.BarWidth > 36)
                    {
                        var pctTb = new TextBlock
                        {
                            Text       = $"{(int)(row.PercentComplete * 100)}%",
                            FontSize   = 9,
                            Foreground = new SolidColorBrush(Colors.White)
                        };
                        Canvas.SetLeft(pctTb, row.BarLeft + 4);
                        Canvas.SetTop(pctTb, top + _barOffsetY + (_barHeight - 12) / 2.0);
                        ChartCanvas.Children.Add(pctTb);
                    }
                }

                if (!string.IsNullOrEmpty(row.AssignedTo) && row.BarWidth > 30)
                {
                    var assignedTb = new TextBlock
                    {
                        Text       = row.AssignedTo,
                        FontSize   = 9,
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xA0, 0x80, 0x80, 0x80))
                    };
                    Canvas.SetLeft(assignedTb, row.BarLeft);
                    Canvas.SetTop(assignedTb, top + _barOffsetY + _barHeight + 1);
                    ChartCanvas.Children.Add(assignedTb);
                }
            }
            else
            {
                var dot = new Ellipse
                {
                    Width  = 5,
                    Height = 5,
                    Fill   = new SolidColorBrush(Windows.UI.Color.FromArgb(0x60, 0x80, 0x80, 0x80))
                };
                Canvas.SetLeft(dot, 6);
                Canvas.SetTop(dot, top + (_rowHeight - 5) / 2.0);
                ChartCanvas.Children.Add(dot);
            }
        }
    }

    // ─── Milestones ───────────────────────────────────────────────────────────

    private void DrawMilestones(double rowsH)
    {
        const double size      = 12.0;
        var          lineBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x1A, 0xFF, 0xB9, 0x00));
        foreach (GanttMilestoneMarker marker in ViewModel.MilestoneMarkers ?? [])
        {
            // Column highlight runs through the content rows
            AddVLine(ChartCanvas, marker.X, 0, rowsH, lineBrush, 1.5);

            // Diamond sits in the sticky header
            var diamond = new Rectangle
            {
                Width           = size,
                Height          = size,
                Fill            = new SolidColorBrush(marker.IsComplete
                    ? Windows.UI.Color.FromArgb(0xFF, 0x10, 0x7C, 0x10)
                    : Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xB9, 0x00)),
                Stroke          = new SolidColorBrush(Windows.UI.Color.FromArgb(0x80, 0x00, 0x00, 0x00)),
                StrokeThickness = 1,
                RenderTransform = new RotateTransform { Angle = 45, CenterX = size / 2, CenterY = size / 2 }
            };
            Canvas.SetLeft(diamond, marker.X - size / 2.0);
            Canvas.SetTop(diamond, _headerTopH + (_headerBottomH - size) / 2.0);
            HeaderCanvas.Children.Add(diamond);
            ToolTipService.SetToolTip(diamond, marker.Title);
        }
    }

    // ─── Dependency arrows ────────────────────────────────────────────────────

    private void DrawDependencies()
    {
        var startBrush  = new SolidColorBrush(Windows.UI.Color.FromArgb(0x80, 0x80, 0x80, 0x80));
        var finishBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0xC0, 0xCA, 0x50, 0x10));
        const double exitGap   = 8.0;
        const double arrowSize = 5.0;

        foreach (GanttDependencyArrow arrow in ViewModel.DependencyArrows ?? [])
        {
            bool isFinish = DependencyConstraints.IsFinishConstraint(arrow.Type);
            var  brush    = isFinish ? finishBrush : startBrush;

            string typeLabel = arrow.Type switch
            {
                DependencyType.FinishToStart  => "Finish → Start",
                DependencyType.StartToStart   => "Start → Start",
                DependencyType.FinishToFinish => "Finish → Finish",
                DependencyType.StartToFinish  => "Start → Finish",
                _                             => string.Empty
            };

            bool   isFromFinish = arrow.Type is DependencyType.FinishToStart or DependencyType.FinishToFinish;
            double exitX        = isFromFinish ? arrow.FromX + exitGap : arrow.FromX - exitGap;
            double fromY        = arrow.FromY - _headerHeight;
            double toY          = arrow.ToY   - _headerHeight;

            var connector = new Polyline
            {
                Stroke          = brush,
                StrokeThickness = 1.5,
                StrokeDashArray = isFinish ? new DoubleCollection { 4, 2 } : null,
                Points          = new PointCollection
                {
                    new(arrow.FromX, fromY),
                    new(exitX,       fromY),
                    new(exitX,       toY),
                    new(arrow.ToX,   toY)
                }
            };
            ToolTipService.SetToolTip(connector, typeLabel);
            ChartCanvas.Children.Add(connector);

            bool pointsRight = arrow.ToX >= exitX;
            var arrowHead = new Polygon
            {
                Fill   = brush,
                Points = pointsRight
                    ? new PointCollection
                      {
                          new(arrow.ToX,             toY),
                          new(arrow.ToX - arrowSize, toY - arrowSize / 2.0),
                          new(arrow.ToX - arrowSize, toY + arrowSize / 2.0)
                      }
                    : new PointCollection
                      {
                          new(arrow.ToX,             toY),
                          new(arrow.ToX + arrowSize, toY - arrowSize / 2.0),
                          new(arrow.ToX + arrowSize, toY + arrowSize / 2.0)
                      }
            };
            ChartCanvas.Children.Add(arrowHead);
            if (!string.IsNullOrEmpty(typeLabel))
                ToolTipService.SetToolTip(arrowHead, typeLabel);
        }
    }

    // ─── Today line ───────────────────────────────────────────────────────────

    private void DrawTodayLine(double rowsH)
    {
        if (ViewModel.TodayX < 0) return;

        var todayBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0xEE, 0xC5, 0x0F, 0x1F));

        // Runs through the sticky header
        AddVLine(HeaderCanvas, ViewModel.TodayX, 0, _headerHeight, todayBrush, 2);

        var todayLabel = new TextBlock
        {
            Text       = "Today",
            FontSize   = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = todayBrush
        };
        Canvas.SetLeft(todayLabel, ViewModel.TodayX + 3);
        Canvas.SetTop(todayLabel, _headerTopH + 4);
        HeaderCanvas.Children.Add(todayLabel);

        // Continues through the scrollable content rows
        AddVLine(ChartCanvas, ViewModel.TodayX, 0, rowsH, todayBrush, 2);
    }

    // ─── Drawing helpers ──────────────────────────────────────────────────────

    private static void AddHLine(Canvas canvas, double x1, double x2, double y,
        SolidColorBrush brush, double thickness)
        => canvas.Children.Add(new Line { X1 = x1, Y1 = y, X2 = x2, Y2 = y, Stroke = brush, StrokeThickness = thickness });

    private static void AddVLine(Canvas canvas, double x, double y1, double y2,
        SolidColorBrush brush, double thickness)
        => canvas.Children.Add(new Line { X1 = x, Y1 = y1, X2 = x, Y2 = y2, Stroke = brush, StrokeThickness = thickness });
}
