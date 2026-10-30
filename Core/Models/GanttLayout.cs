namespace iscWBS.Core.Models;

/// <summary>
/// Pre-computed layout result produced by <see cref="GanttLayoutBuilder"/>. Y values are relative to the
/// rows canvas; the header is drawn on its own canvas and is not included in <see cref="TotalHeight"/>.
/// </summary>
public sealed record GanttLayout(
    IReadOnlyList<GanttRow> Rows,
    IReadOnlyList<GanttHeaderTick> HeaderTicks,
    IReadOnlyList<GanttMilestoneMarker> MilestoneMarkers,
    IReadOnlyList<GanttDependencyArrow> DependencyArrows,
    double TotalWidth,
    double TotalHeight,
    double? TodayX,
    double PixelsPerDay = 40)
{
    public const double RowHeight    = 40.0;
    public const double BarHeight    = 24.0;
    public const double HeaderHeight = 56.0;
}
