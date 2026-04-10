namespace iscWBS.Core.Models;

/// <summary>Pre-computed canvas coordinates for a single dependency connector line.</summary>
/// <param name="FromX">X origin of the arrow on the time axis (right or left edge of predecessor bar, depending on type).</param>
/// <param name="FromY">Y midpoint of the predecessor bar.</param>
/// <param name="ToX">X destination of the arrow on the time axis (left or right edge of successor bar, depending on type).</param>
/// <param name="ToY">Y midpoint of the successor bar.</param>
/// <param name="Type">Dependency type — determines endpoints and visual style.</param>
public sealed record GanttDependencyArrow(
    double FromX,
    double FromY,
    double ToX,
    double ToY,
    DependencyType Type);
