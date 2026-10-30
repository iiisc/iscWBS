namespace iscWBS.Core.Models;

/// <summary>Turns project data into pixel positions for the Gantt chart.</summary>
public static class GanttLayoutBuilder
{
    private static readonly double[] _pixelsPerDayByZoom = { 40.0, 8.0, 2.0 };

    private const double _rowHeight     = GanttLayout.RowHeight;
    private const double _barHeight     = GanttLayout.BarHeight;
    private const double _barOffsetY    = (_rowHeight - _barHeight) / 2.0;
    private const double _minTotalWidth = 600.0;

    /// <param name="zoomIndex">0 = Day, 1 = Week, 2 = Month.</param>
    public static GanttLayout Build(
        IReadOnlyList<WbsNode> nodes,
        IReadOnlyList<Milestone> milestones,
        IReadOnlyList<NodeDependency> dependencies,
        IReadOnlySet<Guid> blockedNodeIds,
        DateTime? projectStartDate,
        int zoomIndex)
    {
        // Everything dated counts, so work before the project's start date or a milestone outside the bars can still be scrolled to.
        DateTime projectStart = nodes.Where(n => n.StartDate.HasValue)
            .Select(n => n.StartDate!.Value.Date)
            .Concat(nodes.Where(n => n.BaselineStart.HasValue).Select(n => n.BaselineStart!.Value.Date))
            .Concat(milestones.Select(m => m.DueDate.Date))
            .Concat(projectStartDate is { } date ? [date.Date] : [])
            .DefaultIfEmpty(DateTime.Today)
            .Min();

        double ppd      = _pixelsPerDayByZoom[zoomIndex];
        double maxRight = 0;

        WbsHierarchy hierarchy = new(nodes);
        Dictionary<Guid, int> levels = hierarchy.BranchLevels();
        IReadOnlySet<Guid> critical = Scheduler.CriticalPath(nodes, dependencies);
        var barPositions = new Dictionary<Guid, (double Left, double Right, double MidY)>();
        var rows = new List<GanttRow>();

        foreach ((WbsNode node, int depth) in hierarchy.DepthFirst())
        {
            double rowTop = rows.Count * _rowHeight;
            bool hasBar   = node.StartDate.HasValue && node.DueDate.HasValue;
            double barLeft = 0, barWidth = 0;

            if (hasBar)
            {
                barLeft  = (node.StartDate!.Value.Date - projectStart).TotalDays * ppd;
                barWidth = Math.Max(4, (node.DueDate!.Value.Date - node.StartDate.Value.Date).TotalDays * ppd);
                maxRight = Math.Max(maxRight, barLeft + barWidth);
                barPositions[node.Id] = (barLeft, barLeft + barWidth, rowTop + _barOffsetY + _barHeight / 2.0);
            }

            bool hasBaseline = node.BaselineStart.HasValue && node.BaselineDue.HasValue;
            double baselineLeft = 0, baselineWidth = 0;
            if (hasBaseline)
            {
                baselineLeft  = (node.BaselineStart!.Value.Date - projectStart).TotalDays * ppd;
                baselineWidth = Math.Max(4, (node.BaselineDue!.Value.Date - node.BaselineStart.Value.Date).TotalDays * ppd);
                maxRight = Math.Max(maxRight, baselineLeft + baselineWidth);
            }

            rows.Add(new GanttRow
            {
                NodeId          = node.Id,
                Label           = $"{node.Code}  {node.Title}",
                RowTop          = rowTop,
                HasBar          = hasBar,
                BarLeft         = barLeft,
                BarWidth        = barWidth,
                Status          = DependencyConstraints.EffectiveStatus(node, blockedNodeIds),
                Depth           = depth,
                Level           = levels[node.Id],
                IsParent        = hierarchy.HasChildren(node.Id),
                AssignedTo      = node.AssignedTo,
                PercentComplete = hierarchy.Progress(node),
                IsCritical      = critical.Contains(node.Id),
                HasBaseline     = hasBaseline,
                BaselineLeft    = baselineLeft,
                BaselineWidth   = baselineWidth,
            });
        }

        IReadOnlyList<GanttMilestoneMarker> markers = BuildMilestoneMarkers(milestones, projectStart, ppd);
        maxRight = markers.Select(m => m.X).Append(maxRight).Max();

        double totalHeight = rows.Count * _rowHeight + 40;
        double totalWidth  = Math.Max(_minTotalWidth, maxRight + 120);
        double todayX      = (DateTime.Today - projectStart).TotalDays * ppd;

        return new GanttLayout(
            rows,
            BuildHeaderTicks(projectStart, ppd, totalWidth, zoomIndex),
            markers,
            BuildDependencyArrows(dependencies, barPositions),
            totalWidth,
            totalHeight,
            todayX >= 0 && todayX <= totalWidth ? todayX : null,
            ppd);
    }

    private static IReadOnlyList<GanttHeaderTick> BuildHeaderTicks(
        DateTime projectStart, double ppd, double totalWidth, int zoomIndex)
    {
        double   endDays = totalWidth / ppd + 1;
        DateTime end     = projectStart.AddDays(endDays);
        var      ticks   = new List<GanttHeaderTick>();

        switch (zoomIndex)
        {
            case 0 or 1: // Day / Week — major boundary = month change
            {
                bool    isDay     = zoomIndex == 0;
                string? lastMajor = null;
                for (DateTime d = projectStart; d <= end; d = d.AddDays(isDay ? 1 : 7))
                {
                    double x      = (d - projectStart).TotalDays * ppd;
                    string major  = d.ToString("MMMM yyyy");
                    string minor  = isDay ? d.Day.ToString() : d.ToString("d MMM");
                    ticks.Add(new GanttHeaderTick(x, minor, major != lastMajor ? major : null));
                    lastMajor = major;
                }
                break;
            }
            case 2: // Month — minor = month abbreviation; major boundary = year change
            {
                string? lastMajor    = null;
                bool    firstVisible = false;
                var     d            = new DateTime(projectStart.Year, projectStart.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                while (d <= end)
                {
                    double x      = (d - projectStart).TotalDays * ppd;
                    string major  = d.ToString("yyyy");
                    bool boundary = major != lastMajor || !firstVisible;
                    lastMajor = major;
                    if (x >= 0)
                    {
                        ticks.Add(new GanttHeaderTick(x, d.ToString("MMM"), boundary ? major : null));
                        firstVisible = true;
                    }
                    d = d.AddMonths(1);
                }
                break;
            }
        }

        return ticks;
    }

    private static IReadOnlyList<GanttMilestoneMarker> BuildMilestoneMarkers(
        IReadOnlyList<Milestone> milestones, DateTime projectStart, double ppd)
    {
        return milestones
            .Select(m => new GanttMilestoneMarker(
                m.Title,
                (m.DueDate.Date - projectStart).TotalDays * ppd,
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
