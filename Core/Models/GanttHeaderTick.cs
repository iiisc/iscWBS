namespace iscWBS.Core.Models;

/// <summary>A single tick mark on the Gantt timeline ruler.</summary>
/// <param name="X">Chart-relative X position in pixels.</param>
/// <param name="Label">Minor (bottom-tier) label — day number, week date, or month abbreviation.</param>
/// <param name="MajorLabel">Major (top-tier) label when this tick starts a new month or year; otherwise null.</param>
public sealed record GanttHeaderTick(
    double X,
    string Label,
    string? MajorLabel = null);
