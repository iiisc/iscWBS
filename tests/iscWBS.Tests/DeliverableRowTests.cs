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
    public void Progress_CountsWholeSubtree_OrOnlyCountedIdsWhenGiven()
    {
        WbsNode d = Deliverable("1", 0);
        WbsNode done1 = Node(WbsStatus.Complete, d, 0);
        WbsNode open = Node(WbsStatus.NotStarted, d, 1);
        WbsNode done2 = Node(WbsStatus.Complete, d, 2);
        WbsNode[] nodes = [d, done1, open, done2];

        Assert.Equal(0.5, DeliverableRow.Build(nodes, NoneBlocked).Single().ProgressFraction);
        Assert.Equal(1.0, DeliverableRow.Build(nodes, NoneBlocked, new HashSet<Guid> { done1.Id, done2.Id }).Single().ProgressFraction);
        Assert.Equal(0.0, DeliverableRow.Build(nodes, NoneBlocked, new HashSet<Guid>()).Single().ProgressFraction);
    }

    [Fact]
    public void DueDate_IsLatestInFullSubtree_EvenWhenThatNodeIsNotCounted()
    {
        WbsNode d = Deliverable("1", 0);
        d.DueDate = new DateTime(2026, 3, 1);
        WbsNode counted = Node(WbsStatus.Complete, d);
        counted.DueDate = new DateTime(2026, 4, 1);
        WbsNode uncountedGrandchild = Node(WbsStatus.NotStarted, counted);
        uncountedGrandchild.DueDate = new DateTime(2026, 6, 1);

        DeliverableRow row = DeliverableRow.Build(
            [d, counted, uncountedGrandchild], NoneBlocked, new HashSet<Guid> { counted.Id }).Single();

        Assert.Equal(new DateTime(2026, 6, 1), row.DueDate);
    }

    [Fact]
    public void BlockedDeliverable_ReportsBlocked_OthersKeepTheirStatus()
    {
        WbsNode blocked = Deliverable("1", 0, WbsStatus.NotStarted);
        WbsNode free = Deliverable("2", 1, WbsStatus.InProgress);

        IReadOnlyList<DeliverableRow> rows = DeliverableRow.Build([blocked, free], new HashSet<Guid> { blocked.Id });

        Assert.Equal([WbsStatus.Blocked, WbsStatus.InProgress], rows.Select(r => r.Status));
    }
}
