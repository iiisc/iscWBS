using iscWBS.Core.Models;
using static iscWBS.Tests.TestData;

namespace iscWBS.Tests;

public class GanttLayoutBuilderTests
{
    private const int Day = 0, Week = 1, Month = 2;
    private static readonly DateTime Start = new(2026, 1, 1);
    private static readonly HashSet<Guid> NoneBlocked = [];

    private static GanttLayout Build(
        IReadOnlyList<WbsNode> nodes,
        int zoom = Day,
        DateTime? projectStart = null,
        IReadOnlyList<NodeDependency>? dependencies = null,
        IReadOnlyList<Milestone>? milestones = null,
        IReadOnlySet<Guid>? blocked = null)
        => GanttLayoutBuilder.Build(nodes, milestones ?? [], dependencies ?? [], blocked ?? NoneBlocked, projectStart, zoom);

    private static WbsNode Bar(DateTime start, DateTime due, WbsNode? parent = null, int sortOrder = 0)
    {
        WbsNode node = Node(parent: parent, sortOrder: sortOrder);
        node.StartDate = start;
        node.DueDate = due;
        return node;
    }

    [Fact]
    public void Rows_FollowTreeOrder_WithDepthAndStackedTops()
    {
        WbsNode rootB = new() { Code = "2", Title = "B", SortOrder = 1 };
        WbsNode rootA = new() { Code = "1", Title = "A", SortOrder = 0 };
        WbsNode a2 = new() { Code = "1.2", Title = "A2", ParentId = rootA.Id, SortOrder = 1 };
        WbsNode a1 = new() { Code = "1.1", Title = "A1", ParentId = rootA.Id, SortOrder = 0 };

        GanttLayout layout = Build([rootB, a2, rootA, a1], projectStart: Start);

        Assert.Equal(["1  A", "1.1  A1", "1.2  A2", "2  B"], layout.Rows.Select(r => r.Label));
        Assert.Equal([0, 1, 1, 0], layout.Rows.Select(r => r.Depth));
        Assert.Equal([true, false, false, false], layout.Rows.Select(r => r.IsParent));
        Assert.Equal(
            Enumerable.Range(0, 4).Select(i => i * GanttLayout.RowHeight),
            layout.Rows.Select(r => r.RowTop));
        Assert.Equal(4 * GanttLayout.RowHeight + 40, layout.TotalHeight);
    }

    [Theory]
    [InlineData(Day, 40.0)]
    [InlineData(Week, 8.0)]
    [InlineData(Month, 2.0)]
    public void Bar_PositionAndWidth_ScaleWithZoom(int zoom, double pixelsPerDay)
    {
        WbsNode node = Bar(Start.AddDays(10), Start.AddDays(20));

        GanttRow row = Build([node], zoom, Start).Rows.Single();

        Assert.True(row.HasBar);
        Assert.Equal(10 * pixelsPerDay, row.BarLeft);
        Assert.Equal(10 * pixelsPerDay, row.BarWidth);
    }

    [Fact]
    public void Bar_HasMinimumWidth_AndNodesWithoutBothDatesHaveNoBar()
    {
        WbsNode sameDay = Bar(Start.AddDays(3), Start.AddDays(3), sortOrder: 0);
        WbsNode noDue = Node(sortOrder: 1);
        noDue.StartDate = Start;

        GanttLayout layout = Build([sameDay, noDue], Month, Start);

        Assert.Equal(4, layout.Rows[0].BarWidth);
        Assert.False(layout.Rows[1].HasBar);
    }

    [Fact]
    public void ProjectStart_FallsBackToEarliestNodeStart()
    {
        WbsNode later = Bar(new DateTime(2026, 3, 10), new DateTime(2026, 3, 20), sortOrder: 0);
        WbsNode earliest = Bar(new DateTime(2026, 3, 5), new DateTime(2026, 3, 8), sortOrder: 1);

        GanttLayout layout = Build([later, earliest], Day, projectStart: null);

        Assert.Equal(5 * 40.0, layout.Rows[0].BarLeft);
        Assert.Equal(0, layout.Rows[1].BarLeft);
    }

    [Fact]
    public void ProjectStart_FallsBackToToday_WhenNoNodeHasAStartDate()
        => Assert.Equal(0, Build([Node()], Day, projectStart: null).TodayX);

    [Fact]
    public void TotalWidth_HasAMinimum_AndOtherwiseFitsTheRightmostBar()
    {
        Assert.Equal(600, Build([Bar(Start, Start.AddDays(1))], Day, Start).TotalWidth);
        Assert.Equal(30 * 40.0 + 120, Build([Bar(Start, Start.AddDays(30))], Day, Start).TotalWidth);
    }

    [Fact]
    public void BlockedIds_OverrideRowStatus()
    {
        WbsNode node = Node(WbsStatus.NotStarted);

        GanttLayout layout = Build([node], Day, Start, blocked: new HashSet<Guid> { node.Id });

        Assert.Equal(WbsStatus.Blocked, layout.Rows.Single().Status);
    }

    [Fact]
    public void DayTicks_OnePerDay_MajorLabelAtStartAndEachNewMonth()
    {
        DateTime start = new(2026, 1, 30);

        IReadOnlyList<GanttHeaderTick> ticks = Build([], Day, start).HeaderTicks;

        Assert.Equal([0.0, 40.0, 80.0, 120.0], ticks.Take(4).Select(t => t.X));
        Assert.Equal(["30", "31", "1", "2"], ticks.Take(4).Select(t => t.Label));
        Assert.Equal(
            [new DateTime(2026, 1, 1).ToString("MMMM yyyy"), null, new DateTime(2026, 2, 1).ToString("MMMM yyyy"), null],
            ticks.Take(4).Select(t => t.MajorLabel));
    }

    [Fact]
    public void WeekTicks_EverySevenDays_MajorLabelWhenMonthChanges()
    {
        DateTime start = new(2026, 1, 20);

        IReadOnlyList<GanttHeaderTick> ticks = Build([], Week, start).HeaderTicks;

        Assert.Equal([0.0, 56.0, 112.0], ticks.Take(3).Select(t => t.X));
        Assert.Equal(
            [start.ToString("d MMM"), start.AddDays(7).ToString("d MMM"), start.AddDays(14).ToString("d MMM")],
            ticks.Take(3).Select(t => t.Label));
        Assert.Equal([true, false, true], ticks.Take(3).Select(t => t.MajorLabel is not null));
    }

    [Fact]
    public void MonthTicks_SkipMonthBeforeStart_YearOnFirstVisibleTickAndEachNewYear()
    {
        DateTime start = new(2026, 11, 15);

        IReadOnlyList<GanttHeaderTick> ticks = Build([], Month, start).HeaderTicks;

        Assert.Equal([16 * 2.0, 47 * 2.0, 78 * 2.0], ticks.Take(3).Select(t => t.X));
        Assert.Equal(
            [new DateTime(2026, 12, 1).ToString("MMM"), new DateTime(2027, 1, 1).ToString("MMM"), new DateTime(2027, 2, 1).ToString("MMM")],
            ticks.Take(3).Select(t => t.Label));
        Assert.Equal(["2026", "2027", null], ticks.Take(3).Select(t => t.MajorLabel));
    }

    [Theory]
    [InlineData(DependencyType.FinishToStart, 200.0, 400.0)]
    [InlineData(DependencyType.StartToStart, 0.0, 400.0)]
    [InlineData(DependencyType.FinishToFinish, 200.0, 600.0)]
    [InlineData(DependencyType.StartToFinish, 0.0, 600.0)]
    public void DependencyArrow_ConnectsTheEdgesImpliedByItsType(DependencyType type, double fromX, double toX)
    {
        WbsNode pred = Bar(Start, Start.AddDays(5), sortOrder: 0);
        WbsNode succ = Bar(Start.AddDays(10), Start.AddDays(15), sortOrder: 1);

        GanttDependencyArrow arrow = Build([pred, succ], Day, Start, [Dep(pred, succ, type)]).DependencyArrows.Single();

        double barMid = (GanttLayout.RowHeight - GanttLayout.BarHeight) / 2 + GanttLayout.BarHeight / 2;
        Assert.Equal(new GanttDependencyArrow(fromX, barMid, toX,
            GanttLayout.RowHeight + barMid, type), arrow);
    }

    [Fact]
    public void DependencyArrow_OmittedWhenAnEndpointHasNoBar()
    {
        WbsNode pred = Bar(Start, Start.AddDays(5), sortOrder: 0);
        WbsNode undated = Node(sortOrder: 1);

        Assert.Empty(Build([pred, undated], Day, Start, [Dep(pred, undated)]).DependencyArrows);
    }

    [Fact]
    public void MilestoneMarkers_PlacedByDueDate()
    {
        Milestone milestone = new() { Title = "Go-live", DueDate = Start.AddDays(7), IsComplete = true };

        GanttMilestoneMarker marker = Build([], Week, Start, milestones: [milestone]).MilestoneMarkers.Single();

        Assert.Equal(new GanttMilestoneMarker("Go-live", 56.0, true), marker);
    }

    [Fact]
    public void MilestonesBeforeAndAfterTheBars_StayInsideTheChart()
    {
        WbsNode task = Bar(new DateTime(2027, 1, 1), new DateTime(2027, 1, 31));
        Milestone kickoff = new() { Title = "Kickoff", DueDate = new DateTime(2026, 12, 15) };
        Milestone launch = new() { Title = "Launch", DueDate = new DateTime(2027, 3, 1) };

        GanttLayout layout = Build([task], Day, milestones: [kickoff, launch]);

        Assert.All(layout.MilestoneMarkers, m => Assert.InRange(m.X, 0, layout.TotalWidth));
        Assert.Contains(layout.HeaderTicks, t => t.X >= layout.MilestoneMarkers.Max(m => m.X));
    }
}
