using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
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
    private const double _labelWidth   = 220;
    private const double _barOffsetY   = 12;
    private const double _barHeight    = 24;

    private readonly Canvas _ganttCanvas = new();

    public GanttViewModel ViewModel { get; }

    public GanttPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<GanttViewModel>();
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
        _ganttCanvas.Width = ViewModel.TotalWidth;
        _ganttCanvas.Height = ViewModel.TotalHeight;
        _ganttCanvas.Children.Clear();

        var labelBrush = new SolidColorBrush(ActualTheme == ElementTheme.Dark
            ? Colors.White : Colors.Black);

        foreach (GanttRowViewModel row in ViewModel.Rows)
        {
            bool even = (int)(row.RowTop / _rowHeight) % 2 == 0;
            var bg = new Rectangle
            {
                Width = ViewModel.TotalWidth,
                Height = _rowHeight,
                Fill = new SolidColorBrush(even
                    ? Windows.UI.Color.FromArgb(0x10, 0x80, 0x80, 0x80)
                    : Windows.UI.Color.FromArgb(0x05, 0x80, 0x80, 0x80))
            };
            Canvas.SetTop(bg, row.RowTop);
            Canvas.SetLeft(bg, 0);
            _ganttCanvas.Children.Add(bg);

            var label = new TextBlock
            {
                Text = row.Label,
                Width = _labelWidth - 8,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = labelBrush,
                FontSize = 12
            };
            Canvas.SetLeft(label, 4);
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
                    Width = row.BarWidth,
                    Height = _barHeight,
                    Fill = new SolidColorBrush(barColor),
                    RadiusX = 4,
                    RadiusY = 4
                };
                Canvas.SetLeft(bar, _labelWidth + row.BarLeft);
                Canvas.SetTop(bar, row.RowTop + _barOffsetY);
                _ganttCanvas.Children.Add(bar);
            }
            else
            {
                var unscheduled = new TextBlock
                {
                    Text = "Unscheduled",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(
                        Windows.UI.Color.FromArgb(0x80, 0x80, 0x80, 0x80))
                };
                Canvas.SetLeft(unscheduled, _labelWidth + 4);
                Canvas.SetTop(unscheduled, row.RowTop + (_rowHeight - 16) / 2);
                _ganttCanvas.Children.Add(unscheduled);
            }
        }

        if (ViewModel.TodayX >= 0)
        {
            var todayLine = new Line
            {
                X1 = _labelWidth + ViewModel.TodayX,
                Y1 = 0,
                X2 = _labelWidth + ViewModel.TodayX,
                Y2 = ViewModel.TotalHeight,
                Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(0xDD, 0xC5, 0x0F, 0x1F)),
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 4, 2 }
            };
            _ganttCanvas.Children.Add(todayLine);
        }
    }
}

