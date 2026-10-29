using iscWBS.Core.Models;
using static iscWBS.Tests.TestData;

namespace iscWBS.Tests;

public class DeliverableRowTests
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

        IReadOnlyList<DeliverableRow> rows = DeliverableRow.Build([second, notDeliverable, first], NoneBlocked);

        Assert.Equal(["1", "2"], rows.Select(r => r.Code));
    }

    [Fact]
    public void MatchingIds_LeaveOutDeliverablesWithNothingMatching_ProgressStillCountsWholeSubtree()
    {
        WbsNode d1 = Deliverable("1", 0);
        WbsNode done1 = Node(WbsStatus.Complete, d1, 0);
        WbsNode open = Node(WbsStatus.NotStarted, d1, 1);
        WbsNode done2 = Node(WbsStatus.Complete, d1, 2);
        WbsNode d2 = Deliverable("2", 1);
        WbsNode other = Node(WbsStatus.NotStarted, d2, 0);
        WbsNode[] nodes = [d1, done1, open, done2, d2, other];

        Assert.Equal(["1", "2"], DeliverableRow.Build(nodes, NoneBlocked).Select(r => r.Code));
        DeliverableRow row = DeliverableRow.Build(nodes, NoneBlocked, new HashSet<Guid> { open.Id }).Single();
        Assert.Equal("1", row.Code);
        Assert.Equal(0.5, row.ProgressFraction);
    }

    [Fact]
    public void DueDate_IsLatestInFullSubtree_EvenWhenThatNodeDoesNotMatch()
    {
        WbsNode d = Deliverable("1", 0);
        d.DueDate = new DateTime(2026, 3, 1);
        WbsNode matching = Node(WbsStatus.Complete, d);
        matching.DueDate = new DateTime(2026, 4, 1);
        WbsNode otherGrandchild = Node(WbsStatus.NotStarted, matching);
        otherGrandchild.DueDate = new DateTime(2026, 6, 1);

        DeliverableRow row = DeliverableRow.Build(
            [d, matching, otherGrandchild], NoneBlocked, new HashSet<Guid> { matching.Id }).Single();

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

        Assert.Equal(["1.2", "2.1"], DeliverableRow.Build([d21, root2, d12, first, root1], NoneBlocked).Select(r => r.Code));
    }

    [Fact]
    public void LeafDeliverable_ProgressComesFromHours_LikeEverywhereElse()
    {
        WbsNode leaf = Deliverable("1", 0);
        leaf.EstimatedHours = 10;
        leaf.ActualHours = 8;

        Assert.Equal(0.8, DeliverableRow.Build([leaf], NoneBlocked).Single().ProgressFraction);
    }

    [Fact]
    public void ProgressPercent_IsRounded()
        => Assert.Equal(58, new DeliverableRow("1", "", 29 / 50.0, WbsStatus.InProgress, null).ProgressPercent);

    [Fact]
    public void BlockedDeliverable_ReportsBlocked_OthersKeepTheirStatus()
    {
        WbsNode blocked = Deliverable("1", 0, WbsStatus.NotStarted);
        WbsNode free = Deliverable("2", 1, WbsStatus.InProgress);

        IReadOnlyList<DeliverableRow> rows = DeliverableRow.Build([blocked, free], new HashSet<Guid> { blocked.Id });

        Assert.Equal([WbsStatus.Blocked, WbsStatus.InProgress], rows.Select(r => r.Status));
    }
}
