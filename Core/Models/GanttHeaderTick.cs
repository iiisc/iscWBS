namespace iscWBS.Core.Models;

/// <summary>A single tick mark on the Gantt timeline ruler.</summary>
/// <param name="X">Chart-relative X position in pixels.</param>
/// <param name="Label">Minor (bottom-tier) label — day number, week date, or month abbreviation.</param>
/// <param name="MajorLabel">Major (top-tier) label shown at this boundary; null if not a boundary.</param>
/// <param name="IsMajorBoundary">True when this tick starts a new major period (month or year).</param>
public sealed record GanttHeaderTick(
    double X,
    string Label,
    string? MajorLabel       = null,
    bool   IsMajorBoundary   = false);
