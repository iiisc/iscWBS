namespace iscWBS.Core.Models;

/// <summary>Pre-computed layout result produced by <see cref="iscWBS.Core.Services.IGanttLayoutService"/>.</summary>
public sealed record GanttLayout(
    IReadOnlyList<GanttRow> Rows,
    IReadOnlyList<GanttHeaderTick> HeaderTicks,
    IReadOnlyList<GanttMilestoneMarker> MilestoneMarkers,
    IReadOnlyList<GanttDependencyArrow> DependencyArrows,
    double TotalWidth,
    double TotalHeight,
    double TodayX);
