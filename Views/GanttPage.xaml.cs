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
    private static readonly Windows.UI.Color _colorNotStarted = Windows.UI.Color.FromArgb(0xCC, 0x80, 0x80, 0x80);
    private static readonly Windows.UI.Color _colorInProgress  = Windows.UI.Color.FromArgb(0xCC, 0x00, 0x78, 0xD4);
    private static readonly Windows.UI.Color _colorComplete    = Windows.UI.Color.FromArgb(0xCC, 0x10, 0x7C, 0x10);
    private static readonly Windows.UI.Color _colorBlocked     = Windows.UI.Color.FromArgb(0xCC, 0xC5, 0x0F, 0x1F);

    private const double _rowHeight    = 48;
    private const double _labelWidth   = GanttViewModel.LabelWidth;
    private const double _barOffsetY   = 12;
    private const double _barHeight    = 24;
    private const double _headerHeight = GanttViewModel.HeaderHeight;

    private readonly Canvas _ganttCanvas = new();

    public GanttViewModel ViewModel { get; }

    public GanttPage()
    {
        ViewModel = App.Services.GetRequiredService<GanttViewModel>();
        InitializeComponent();
        GanttScrollViewer.Content = _ganttCanvas;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.OnNavigatedTo(e.Parameter);
        ViewModel.Rows.CollectionChanged += (_, _) => DrawGantt();
        ViewModel.PropertyChanged += (_, pe) =>
        {
            if (pe.PropertyName is nameof(GanttViewModel.TotalWidth) or nameof(GanttViewModel.TotalHeight))
                DrawGantt();
        };
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.OnNavigatedFrom();
    }

    private void ZoomDay_Checked(object sender, RoutedEventArgs e) => ViewModel.SelectedZoomIndex = 0;
    private void ZoomWeek_Checked(object sender, RoutedEventArgs e) => ViewModel.SelectedZoomIndex = 1;
    private void ZoomMonth_Checked(object sender, RoutedEventArgs e) => ViewModel.SelectedZoomIndex = 2;

    private void DrawGantt()
    {
        _ganttCanvas.Width  = ViewModel.TotalWidth;
        _ganttCanvas.Height = ViewModel.TotalHeight;
        _ganttCanvas.Children.Clear();

        var labelBrush = new SolidColorBrush(ActualTheme == ElementTheme.Dark
            ? Colors.White : Colors.Black);

        DrawHeader(labelBrush);
        DrawRows(labelBrush);
        DrawMilestones();
        DrawDependencies();
        DrawTodayLine();
    }

    private void DrawHeader(SolidColorBrush labelBrush)
    {
        var headerBg = new Rectangle
        {
            Width  = ViewModel.TotalWidth,
            Height = _headerHeight,
            Fill   = new SolidColorBrush(ActualTheme == ElementTheme.Dark
                ? Windows.UI.Color.FromArgb(0x30, 0x00, 0x00, 0x00)
                : Windows.UI.Color.FromArgb(0x15, 0x00, 0x00, 0x00))
        };
        Canvas.SetLeft(headerBg, 0);
        Canvas.SetTop(headerBg, 0);
        _ganttCanvas.Children.Add(headerBg);

        var baseline = new Line
        {
            X1 = 0,                    Y1 = _headerHeight,
            X2 = ViewModel.TotalWidth, Y2 = _headerHeight,
            Stroke          = new SolidColorBrush(Windows.UI.Color.FromArgb(0x40, 0x80, 0x80, 0x80)),
            StrokeThickness = 1
        };
        _ganttCanvas.Children.Add(baseline);

        var tasksLabel = new TextBlock
        {
            Text       = "Tasks",
            FontSize   = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = labelBrush
        };
        Canvas.SetLeft(tasksLabel, 4);
        Canvas.SetTop(tasksLabel, (_headerHeight - 14) / 2);
        _ganttCanvas.Children.Add(tasksLabel);

        foreach (GanttHeaderTick tick in ViewModel.HeaderTicks ?? [])
        {
            var tickLine = new Line
            {
                X1 = _labelWidth + tick.X, Y1 = _headerHeight * 0.6,
                X2 = _labelWidth + tick.X, Y2 = _headerHeight,
                Stroke          = new SolidColorBrush(Windows.UI.Color.FromArgb(0x40, 0x80, 0x80, 0x80)),
                StrokeThickness = 1
            };
            _ganttCanvas.Children.Add(tickLine);

            var tickLabel = new TextBlock
            {
                Text       = tick.Label,
                FontSize   = 10,
                Foreground = labelBrush
            };
            Canvas.SetLeft(tickLabel, _labelWidth + tick.X + 2);
            Canvas.SetTop(tickLabel, 2);
            _ganttCanvas.Children.Add(tickLabel);
        }
    }

    private void DrawRows(SolidColorBrush labelBrush)
    {
        foreach (GanttRow row in ViewModel.Rows)
        {
            int rowIndex = (int)((row.RowTop - _headerHeight) / _rowHeight);
            bool even    = rowIndex % 2 == 0;
            var bg = new Rectangle
            {
                Width  = ViewModel.TotalWidth,
                Height = _rowHeight,
                Fill   = new SolidColorBrush(even
                    ? Windows.UI.Color.FromArgb(0x10, 0x80, 0x80, 0x80)
                    : Windows.UI.Color.FromArgb(0x05, 0x80, 0x80, 0x80))
            };
            Canvas.SetTop(bg, row.RowTop);
            Canvas.SetLeft(bg, 0);
            _ganttCanvas.Children.Add(bg);

            double labelLeft = 4 + row.Depth * 12.0;
            var label = new TextBlock
            {
                Text           = row.Label,
                Width          = _labelWidth - labelLeft - 4,
                TextTrimming   = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground     = labelBrush,
                FontSize       = 12,
                FontWeight     = row.IsParent ? FontWeights.SemiBold : FontWeights.Normal
            };
            Canvas.SetLeft(label, labelLeft);
            Canvas.SetTop(label, row.RowTop + (_rowHeight - 16) / 2);
            _ganttCanvas.Children.Add(label);

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
                    RadiusX = 4,
                    RadiusY = 4
                };
                Canvas.SetLeft(bar, _labelWidth + row.BarLeft);
                Canvas.SetTop(bar, row.RowTop + _barOffsetY);
                _ganttCanvas.Children.Add(bar);

                if (row.PercentComplete > 0)
                {
                    var progressFill = new Rectangle
                    {
                        Width   = Math.Max(8, row.BarWidth * row.PercentComplete),
                        Height  = _barHeight,
                        Fill    = new SolidColorBrush(Windows.UI.Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)),
                        RadiusX = 4,
                        RadiusY = 4
                    };
                    Canvas.SetLeft(progressFill, _labelWidth + row.BarLeft);
                    Canvas.SetTop(progressFill, row.RowTop + _barOffsetY);
                    _ganttCanvas.Children.Add(progressFill);
                }

                if (!string.IsNullOrEmpty(row.AssignedTo))
                {
                    var assignedLabel = new TextBlock
                    {
                        Text       = row.AssignedTo,
                        FontSize   = 10,
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xA0, 0x80, 0x80, 0x80))
                    };
                    Canvas.SetLeft(assignedLabel, _labelWidth + row.BarLeft);
                    Canvas.SetTop(assignedLabel, row.RowTop + _barOffsetY + _barHeight + 1);
                    _ganttCanvas.Children.Add(assignedLabel);
                }
            }
            else
            {
                var unscheduled = new TextBlock
                {
                    Text       = "Unscheduled",
                    FontSize   = 11,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0x80, 0x80, 0x80, 0x80))
                };
                Canvas.SetLeft(unscheduled, _labelWidth + 4);
                Canvas.SetTop(unscheduled, row.RowTop + (_rowHeight - 16) / 2);
                _ganttCanvas.Children.Add(unscheduled);
            }
        }
    }

    private void DrawMilestones()
    {
        const double size = 10.0;
        foreach (GanttMilestoneMarker marker in ViewModel.MilestoneMarkers ?? [])
        {
            var diamond = new Rectangle
            {
                Width   = size,
                Height  = size,
                Fill    = new SolidColorBrush(marker.IsComplete
                    ? Windows.UI.Color.FromArgb(0xFF, 0x10, 0x7C, 0x10)
                    : Windows.UI.Color.FromArgb(0xFF, 0x00, 0x78, 0xD4)),
                RenderTransform = new RotateTransform { Angle = 45, CenterX = size / 2, CenterY = size / 2 }
            };
            Canvas.SetLeft(diamond, _labelWidth + marker.X - size / 2);
            Canvas.SetTop(diamond, (_headerHeight - size) / 2);
            ToolTipService.SetToolTip(diamond, marker.Title);
            _ganttCanvas.Children.Add(diamond);
        }
    }

    private void DrawDependencies()
    {
        var startConstraintBrush  = new SolidColorBrush(Windows.UI.Color.FromArgb(0x80, 0x80, 0x80, 0x80));
        var finishConstraintBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0xA0, 0xCA, 0x50, 0x10));
        const double exitGap = 8.0;

        foreach (GanttDependencyArrow arrow in ViewModel.DependencyArrows ?? [])
        {
            bool isFinishConstraint = DependencyConstraints.IsFinishConstraint(arrow.Type);

            string typeLabel = arrow.Type switch
            {
                DependencyType.FinishToStart  => "Finish → Start",
                DependencyType.StartToStart   => "Start → Start",
                DependencyType.FinishToFinish => "Finish → Finish",
                DependencyType.StartToFinish  => "Start → Finish",
                _                             => string.Empty,
            };

            // Anchor the vertical segment just outside the predecessor bar's exit edge so
            // the connector never routes through intermediate task bars.
            // FS/FF exit from the right edge (+gap); SS/SF exit from the left edge (-gap).
            bool isFromFinish = arrow.Type is DependencyType.FinishToStart or DependencyType.FinishToFinish;
            double exitX = isFromFinish ? arrow.FromX + exitGap : arrow.FromX - exitGap;

            var connector = new Polyline
            {
                Stroke          = isFinishConstraint ? finishConstraintBrush : startConstraintBrush,
                StrokeThickness = 1.5,
                StrokeDashArray = isFinishConstraint ? new DoubleCollection { 4, 2 } : null,
                Points          = new PointCollection
                {
                    new(_labelWidth + arrow.FromX, arrow.FromY),
                    new(_labelWidth + exitX,       arrow.FromY),
                    new(_labelWidth + exitX,       arrow.ToY),
                    new(_labelWidth + arrow.ToX,   arrow.ToY),
                }
            };
            ToolTipService.SetToolTip(connector, typeLabel);
            _ganttCanvas.Children.Add(connector);
        }
    }

    private void DrawTodayLine()
    {
        if (ViewModel.TodayX < 0) return;

        var todayLine = new Line
        {
            X1 = _labelWidth + ViewModel.TodayX,
            Y1 = 0,
            X2 = _labelWidth + ViewModel.TodayX,
            Y2 = ViewModel.TotalHeight,
            Stroke          = new SolidColorBrush(Windows.UI.Color.FromArgb(0xDD, 0xC5, 0x0F, 0x1F)),
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 4, 2 }
        };
        _ganttCanvas.Children.Add(todayLine);
    }
}

