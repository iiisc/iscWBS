using iscWBS.Core.Models;
using static iscWBS.Tests.TestData;

namespace iscWBS.Tests;

public class WbsHierarchyTests
{
    [Fact]
    public void CollectSubtree_IsDepthFirstWithChildrenBySortOrder()
    {
        WbsNode root = Node();
        WbsNode second = Node(parent: root, sortOrder: 1);
        WbsNode first = Node(parent: root, sortOrder: 0);
        WbsNode secondChild = Node(parent: second);
        WbsNode firstChild = Node(parent: first);
        WbsNode unrelated = Node();

        var hierarchy = new WbsHierarchy([secondChild, root, second, unrelated, first, firstChild]);

        Assert.Equal([root, first, firstChild, second, secondChild], hierarchy.CollectSubtree(root));
    }

    [Fact]
    public void Progress_OfParent_IsShareOfCompleteNodesInSubtreeIncludingItself()
    {
        WbsNode root = Node(WbsStatus.InProgress);
        WbsNode a = Node(WbsStatus.Complete, root, 0);
        WbsNode b = Node(WbsStatus.InProgress, root, 1);
        WbsNode bChild = Node(WbsStatus.Complete, b);
        // Would give b 99% if hours leaked into parent progress.
        b.EstimatedHours = 100;
        b.ActualHours = 99;

        var hierarchy = new WbsHierarchy([root, a, b, bChild]);

        Assert.Equal(0.5, hierarchy.Progress(root));
        Assert.Equal(0.5, hierarchy.Progress(b));
    }

    [Theory]
    [InlineData(WbsStatus.Complete, 0, 0, 1.0)]
    [InlineData(WbsStatus.Complete, 10, 2, 1.0)]
    [InlineData(WbsStatus.InProgress, 12, 3, 0.25)]
    [InlineData(WbsStatus.InProgress, 10, 25, 1.0)]
    [InlineData(WbsStatus.InProgress, 0, 5, 0.0)]
    [InlineData(WbsStatus.NotStarted, 8, 0, 0.0)]
    public void Progress_OfLeaf(WbsStatus status, double estimated, double actual, double expected)
    {
        WbsNode leaf = Node(status);
        leaf.EstimatedHours = estimated;
        leaf.ActualHours = actual;

        Assert.Equal(expected, new WbsHierarchy([leaf]).Progress(leaf));
    }
}
