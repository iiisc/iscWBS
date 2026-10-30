using iscWBS.Core.Models;
using static iscWBS.Tests.TestData;

namespace iscWBS.Tests;

public class ProgressRowTests
{
    private static readonly HashSet<Guid> NoneBlocked = [];

    private static WbsNode Deliverable(string code, int sortOrder, WbsStatus status = WbsStatus.InProgress)
        => new() { Code = code, SortOrder = sortOrder, Status = status, IsDeliverable = true };

    [Fact]
    public void OnlyDeliverables_InSortOrder()
    {
        WbsNode second = Deliverable("2", 1);
        WbsNode first = Deliverable("1", 0);
        WbsNode notDeliverable = new() { Code = "3", SortOrder = 2 };

        IReadOnlyList<ProgressRow> rows = ProgressRow.Build([second, notDeliverable, first], NoneBlocked, n => n.IsDeliverable);

        Assert.Equal(["1", "2"], rows.Select(r => r.Code));
    }

    [Fact]
    public void EveryIncludedNode_GetsARow_WithItsDepthAndWhetherItIsADeliverable()
    {
        WbsNode d1 = Deliverable("1", 0);
        WbsNode done = Node(WbsStatus.Complete, d1, 0);
        WbsNode open = Node(WbsStatus.NotStarted, d1, 1);
        done.Code = "1.1";
        open.Code = "1.2";

        IReadOnlyList<ProgressRow> rows = ProgressRow.Build([open, d1, done], NoneBlocked, _ => true);

        Assert.Equal([("1", 0, true), ("1.1", 1, false), ("1.2", 1, false)], rows.Select(r => (r.Code, r.Depth, r.IsDeliverable)));
        Assert.Equal(1.0 / 3, rows[0].ProgressFraction, 6);
    }

    [Fact]
    public void DueDate_IsLatestInFullSubtree()
    {
        WbsNode d = Deliverable("1", 0);
        d.DueDate = new DateTime(2026, 3, 1);
        WbsNode matching = Node(WbsStatus.Complete, d);
        matching.DueDate = new DateTime(2026, 4, 1);
        WbsNode otherGrandchild = Node(WbsStatus.NotStarted, matching);
        otherGrandchild.DueDate = new DateTime(2026, 6, 1);

        ProgressRow row = ProgressRow.Build([d, matching, otherGrandchild], NoneBlocked, n => n.IsDeliverable).Single();

        Assert.Equal(new DateTime(2026, 6, 1), row.DueDate);
    }

    [Fact]
    public void Deliverables_AtDifferentLevels_FollowOutlineOrder()
    {
        WbsNode root1 = new() { Code = "1", SortOrder = 0 };
        WbsNode first = new() { Code = "1.1", ParentId = root1.Id, SortOrder = 0 };
        WbsNode d12 = new() { Code = "1.2", ParentId = root1.Id, SortOrder = 1, IsDeliverable = true };
        WbsNode root2 = new() { Code = "2", SortOrder = 1 };
        WbsNode d21 = new() { Code = "2.1", ParentId = root2.Id, SortOrder = 0, IsDeliverable = true };

        Assert.Equal(["1.2", "2.1"], ProgressRow.Build([d21, root2, d12, first, root1], NoneBlocked, n => n.IsDeliverable).Select(r => r.Code));
    }

    [Fact]
    public void LeafDeliverable_ProgressComesFromHours_LikeEverywhereElse()
    {
        WbsNode leaf = Deliverable("1", 0);
        leaf.EstimatedHours = 10;
        leaf.ActualHours = 8;

        Assert.Equal(0.8, ProgressRow.Build([leaf], NoneBlocked, n => n.IsDeliverable).Single().ProgressFraction);
    }

    [Fact]
    public void ProgressPercent_IsRounded()
        => Assert.Equal(58, new ProgressRow("1", "", 29 / 50.0, WbsStatus.InProgress, null).ProgressPercent);

    [Fact]
    public void BlockedDeliverable_ReportsBlocked_OthersKeepTheirStatus()
    {
        WbsNode blocked = Deliverable("1", 0, WbsStatus.NotStarted);
        WbsNode free = Deliverable("2", 1, WbsStatus.InProgress);

        IReadOnlyList<ProgressRow> rows = ProgressRow.Build([blocked, free], new HashSet<Guid> { blocked.Id }, n => n.IsDeliverable);

        Assert.Equal([WbsStatus.Blocked, WbsStatus.InProgress], rows.Select(r => r.Status));
    }
}
