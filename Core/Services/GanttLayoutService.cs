using iscWBS.Core.Models;

namespace iscWBS.Core.Services;

public sealed class GanttLayoutService : IGanttLayoutService
{
    private static readonly double[] _pixelsPerDayByZoom = { 40.0, 8.0, 2.0 };

    private const double _rowHeight    = 48.0;
    private const double _barOffsetY   = 12.0;
    private const double _barHeight    = 24.0;
    private const double _headerHeight = 40.0;
    private const double _labelWidth   = 220.0;
    private const double _minTotalWidth = 800.0;

    public GanttLayout Build(
        IReadOnlyList<WbsNode> nodes,
        IReadOnlyList<Milestone> milestones,
        IReadOnlyList<NodeDependency> dependencies,
        IReadOnlySet<Guid> blockedNodeIds,
        DateTime? projectStartDate,
        int zoomIndex)
    {
        DateTime projectStart = projectStartDate?.Date
            ?? nodes.Where(n => n.StartDate.HasValue)
                    .Select(n => n.StartDate!.Value.Date)
                    .DefaultIfEmpty(DateTime.UtcNow.Date)
                    .Min();

        double ppd      = _pixelsPerDayByZoom[zoomIndex];
        double maxRight = 0;

        var groups = nodes.GroupBy(n => n.ParentId).ToList();

        List<WbsNode> roots = groups
            .FirstOrDefault(g => !g.Key.HasValue)
            ?.OrderBy(n => n.SortOrder).ToList()
            ?? [];

        // Dictionary<Guid, …> — non-nullable key; Dictionary rejects null keys even for Guid?
        var childMap = groups
            .Where(g => g.Key.HasValue)
            .ToDictionary(
                g => g.Key!.Value,
                g => g.OrderBy(n => n.SortOrder).ToList());

        var barPositions = new Dictionary<Guid, (double Left, double Right, double MidY)>();
        var rows = new List<GanttRow>();
        int rowIndex = 0;

        void WalkNode(WbsNode node, int depth)
        {
            double rowTop = _headerHeight + rowIndex * _rowHeight;
            bool isParent = childMap.ContainsKey(node.Id);
            bool hasBar   = node.StartDate.HasValue && node.DueDate.HasValue;
            double barLeft = 0, barWidth = 0;

            if (hasBar)
            {
                barLeft  = (node.StartDate!.Value.Date - projectStart).TotalDays * ppd;
                barWidth = Math.Max(4, (node.DueDate!.Value.Date - node.StartDate.Value.Date).TotalDays * ppd);
                maxRight = Math.Max(maxRight, barLeft + barWidth);
                barPositions[node.Id] = (barLeft, barLeft + barWidth, rowTop + _barOffsetY + _barHeight / 2.0);
            }

            double pc = node.EstimatedHours > 0
                ? Math.Clamp(node.ActualHours / node.EstimatedHours, 0, 1)
                : 0.0;

            rows.Add(new GanttRow
            {
                NodeId          = node.Id,
                Label           = $"{node.Code}  {node.Title}",
                RowTop          = rowTop,
                HasBar          = hasBar,
                BarLeft         = barLeft,
                BarWidth        = barWidth,
                Status          = blockedNodeIds.Contains(node.Id) ? WbsStatus.Blocked : node.Status,
                Depth           = depth,
                IsParent        = isParent,
                AssignedTo      = node.AssignedTo,
                PercentComplete = pc,
            });

            rowIndex++;

            if (childMap.TryGetValue(node.Id, out List<WbsNode>? children))
                foreach (WbsNode child in children)
                    WalkNode(child, depth + 1);
        }

        foreach (WbsNode root in roots)
            WalkNode(root, 0);

        double totalHeight = rowIndex * _rowHeight + _headerHeight + 40;
        double totalWidth  = Math.Max(_minTotalWidth, maxRight + 120);
        double todayX      = (DateTime.UtcNow.Date - projectStart).TotalDays * ppd;

        return new GanttLayout(
            rows,
            BuildHeaderTicks(projectStart, ppd, totalWidth, zoomIndex),
            BuildMilestoneMarkers(milestones, projectStart, ppd),
            BuildDependencyArrows(dependencies, barPositions),
            totalWidth,
            totalHeight,
            todayX);
    }

    private static IReadOnlyList<GanttHeaderTick> BuildHeaderTicks(
        DateTime projectStart, double ppd, double totalWidth, int zoomIndex)
    {
        double endDays = (totalWidth - _labelWidth) / ppd + 1;
        DateTime end   = projectStart.AddDays(endDays);
        var ticks      = new List<GanttHeaderTick>();

        switch (zoomIndex)
        {
            case 0:
                for (DateTime d = projectStart; d <= end; d = d.AddDays(1))
                    ticks.Add(new GanttHeaderTick((d - projectStart).TotalDays * ppd, d.ToString("d MMM")));
                break;
            case 1:
                for (DateTime d = projectStart; d <= end; d = d.AddDays(7))
                    ticks.Add(new GanttHeaderTick((d - projectStart).TotalDays * ppd, d.ToString("d MMM")));
                break;
            case 2:
                var monthStart = new DateTime(projectStart.Year, projectStart.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                for (DateTime d = monthStart; d <= end; d = d.AddMonths(1))
                    ticks.Add(new GanttHeaderTick((d - projectStart).TotalDays * ppd, d.ToString("MMM yyyy")));
                break;
        }

        return ticks;
    }

    private static IReadOnlyList<GanttMilestoneMarker> BuildMilestoneMarkers(
        IReadOnlyList<Milestone> milestones, DateTime projectStart, double ppd)
    {
        return milestones
            .Select(m => new GanttMilestoneMarker(
                m.Title,
                (DateTime.SpecifyKind(m.DueDate, DateTimeKind.Utc).Date - projectStart).TotalDays * ppd,
                m.IsComplete))
            .ToList();
    }

    private static IReadOnlyList<GanttDependencyArrow> BuildDependencyArrows(
        IReadOnlyList<NodeDependency> dependencies,
        Dictionary<Guid, (double Left, double Right, double MidY)> barPositions)
    {
        var arrows = new List<GanttDependencyArrow>();
        foreach (NodeDependency dep in dependencies)
        {
            if (barPositions.TryGetValue(dep.PredecessorId, out var pred) &&
                barPositions.TryGetValue(dep.SuccessorId,   out var succ))
            {
                (double fromX, double toX) = dep.Type switch
                {
                    DependencyType.FinishToStart  => (pred.Right, succ.Left),
                    DependencyType.StartToStart   => (pred.Left,  succ.Left),
                    DependencyType.FinishToFinish => (pred.Right, succ.Right),
                    DependencyType.StartToFinish  => (pred.Left,  succ.Right),
                    _                             => (pred.Right, succ.Left),
                };
                arrows.Add(new GanttDependencyArrow(fromX, pred.MidY, toX, succ.MidY, dep.Type));
            }
        }
        return arrows;
    }
}
