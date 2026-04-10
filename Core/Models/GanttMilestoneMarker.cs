namespace iscWBS.Core.Models;

/// <summary>A milestone diamond rendered in the Gantt header strip.</summary>
public sealed record GanttMilestoneMarker(string Title, double X, bool IsComplete);
