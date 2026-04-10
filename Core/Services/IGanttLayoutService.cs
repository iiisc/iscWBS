using iscWBS.Core.Models;

namespace iscWBS.Core.Services;

/// <summary>Computes pixel-layout data for the Gantt chart from raw project data.</summary>
public interface IGanttLayoutService
{
    /// <summary>
    /// Builds Gantt rows, header ticks, milestone markers, and dependency arrows from
    /// the supplied project data and zoom level.
    /// </summary>
    /// <param name="nodes">All WBS nodes for the project, ordered by sort order.</param>
    /// <param name="milestones">All milestones for the project.</param>
    /// <param name="dependencies">All node dependencies for the project.</param>
    /// <param name="blockedNodeIds">Pre-resolved set of start-blocked node IDs.</param>
    /// <param name="projectStartDate">The project's configured start date, or <see langword="null"/> to derive it from node dates.</param>
    /// <param name="zoomIndex">Zero-based zoom level index: 0 = Day, 1 = Week, 2 = Month.</param>
    GanttLayout Build(
        IReadOnlyList<WbsNode> nodes,
        IReadOnlyList<Milestone> milestones,
        IReadOnlyList<NodeDependency> dependencies,
        IReadOnlySet<Guid> blockedNodeIds,
        DateTime? projectStartDate,
        int zoomIndex);
}
