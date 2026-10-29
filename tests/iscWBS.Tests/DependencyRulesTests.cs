using iscWBS.Core.Models;
using static iscWBS.Core.Models.DependencyType;
using static iscWBS.Core.Models.WbsStatus;
using static iscWBS.Tests.TestData;

namespace iscWBS.Tests;

public class DependencyRulesTests
{
    [Theory]
    [InlineData(FinishToStart, InProgress, NotStarted, true)]
    [InlineData(FinishToStart, Complete, NotStarted, false)]
    [InlineData(FinishToStart, NotStarted, InProgress, false)]
    [InlineData(FinishToStart, NotStarted, Complete, false)]
    [InlineData(StartToStart, NotStarted, NotStarted, true)]
    [InlineData(StartToStart, InProgress, NotStarted, false)]
    [InlineData(FinishToFinish, NotStarted, NotStarted, false)]
    [InlineData(StartToFinish, NotStarted, NotStarted, false)]
    public void Blocked_OnlyStartConstraints_OnNodesNotYetActive(
        DependencyType type, WbsStatus predStatus, WbsStatus succStatus, bool blocked)
    {
        WbsNode pred = Node(predStatus), succ = Node(succStatus);

        IReadOnlySet<Guid> ids = DependencyConstraints.BlockedNodeIds([pred, succ], [Dep(pred, succ, type)]);

        Assert.Equal(blocked, ids.Contains(succ.Id));
    }

    [Fact]
    public void Blocked_IgnoresDependenciesOnUnknownNodes()
    {
        WbsNode succ = Node();

        Assert.Empty(DependencyConstraints.BlockedNodeIds([succ], [Dep(Node(), succ)]));
    }

    [Theory]
    [InlineData(FinishToFinish, InProgress, InProgress, true)]
    [InlineData(FinishToFinish, Complete, InProgress, false)]
    [InlineData(FinishToFinish, InProgress, Complete, false)]
    [InlineData(StartToFinish, NotStarted, InProgress, true)]
    [InlineData(StartToFinish, InProgress, InProgress, false)]
    [InlineData(FinishToStart, NotStarted, InProgress, false)]
    public void CompletionBlocked_OnlyFinishConstraints_OnIncompleteNodes(
        DependencyType type, WbsStatus predStatus, WbsStatus succStatus, bool blocked)
    {
        WbsNode pred = Node(predStatus), succ = Node(succStatus);

        IReadOnlySet<Guid> ids = DependencyConstraints.CompletionBlockedNodeIds([pred, succ], [Dep(pred, succ, type)]);

        Assert.Equal(blocked, ids.Contains(succ.Id));
    }

    [Fact]
    public void CompletionBlocked_ExcludesNodesThatAreAlsoStartBlocked()
    {
        WbsNode fsPred = Node(NotStarted), ffPred = Node(NotStarted), succ = Node(NotStarted);
        WbsNode[] nodes = [fsPred, ffPred, succ];
        NodeDependency[] deps = [Dep(fsPred, succ, FinishToStart), Dep(ffPred, succ, FinishToFinish)];

        Assert.Contains(succ.Id, DependencyConstraints.BlockedNodeIds(nodes, deps));
        Assert.DoesNotContain(succ.Id, DependencyConstraints.CompletionBlockedNodeIds(nodes, deps));
    }

    [Theory]
    [InlineData(FinishToStart, InProgress, true)]
    [InlineData(FinishToFinish, NotStarted, true)]
    [InlineData(StartToStart, InProgress, false)]
    [InlineData(FinishToStart, Complete, false)]
    public void ViolatedComplete_FlagsCompleteNodesWithAnyViolatedConstraint(
        DependencyType type, WbsStatus predStatus, bool violated)
    {
        WbsNode pred = Node(predStatus), succ = Node(Complete), openSucc = Node(NotStarted);

        IReadOnlySet<Guid> ids = DependencyConstraints.ViolatedCompleteNodeIds(
            [pred, succ, openSucc], [Dep(pred, succ, type), Dep(pred, openSucc, type)]);

        Assert.Equal(violated, ids.Contains(succ.Id));
        Assert.DoesNotContain(openSucc.Id, ids);
    }

    [Theory]
    [InlineData(FinishToStart, NotStarted, true)]
    [InlineData(StartToStart, Blocked, true)]
    [InlineData(StartToStart, InProgress, false)]
    [InlineData(FinishToFinish, NotStarted, false)]
    public void ViolatedInProgress_FlagsInProgressNodesWithViolatedStartConstraint(
        DependencyType type, WbsStatus predStatus, bool violated)
    {
        WbsNode pred = Node(predStatus), succ = Node(InProgress), doneSucc = Node(Complete);

        IReadOnlySet<Guid> ids = DependencyConstraints.ViolatedInProgressNodeIds(
            [pred, succ, doneSucc], [Dep(pred, succ, type), Dep(pred, doneSucc, type)]);

        Assert.Equal(violated, ids.Contains(succ.Id));
        Assert.DoesNotContain(doneSucc.Id, ids);
    }

    [Fact]
    public void ProjectSummary_CountsEachCategory()
    {
        DateTime today = DateTime.Today;
        WbsNode blocked = Node(NotStarted);
        WbsNode blockedInProgress = Node(InProgress);
        WbsNode[] nodes =
        [
            Node(Complete),
            new() { Status = Complete, DueDate = today.AddDays(-3) },
            Node(InProgress),
            blocked,
            blockedInProgress,
            new() { Status = InProgress, DueDate = today.AddDays(-1) },
            new() { Status = NotStarted, DueDate = today.AddDays(5) },
        ];

        ProjectSummary summary = ProjectSummary.Compute(nodes, new HashSet<Guid> { blocked.Id, blockedInProgress.Id });

        Assert.Equal(new ProjectSummary(
            TotalNodes: 7, CompleteCount: 2, InProgressCount: 3, BlockedCount: 2,
            OverdueCount: 1, AtRiskCount: 1, PercentComplete: 28.6), summary);
    }

    [Fact]
    public void ProjectSummary_RoundsPercentToOneDecimal_AndHandlesEmptyProject()
    {
        Assert.Equal(33.3, ProjectSummary.Compute([Node(Complete), Node(), Node()], new HashSet<Guid>()).PercentComplete);
        Assert.Equal(0, ProjectSummary.Compute([], new HashSet<Guid>()).PercentComplete);
    }

    [Theory]
    [InlineData(InProgress, FinishToStart, NotStarted, true)]
    [InlineData(InProgress, StartToStart, InProgress, false)]
    [InlineData(InProgress, FinishToFinish, NotStarted, false)]
    [InlineData(Complete, FinishToFinish, InProgress, true)]
    [InlineData(Complete, StartToFinish, NotStarted, true)]
    [InlineData(Complete, FinishToStart, InProgress, true)]
    [InlineData(Complete, StartToStart, InProgress, false)]
    [InlineData(NotStarted, FinishToStart, NotStarted, false)]
    [InlineData(Blocked, FinishToStart, NotStarted, false)]
    public void TransitionBlockers_ReturnsViolatedDependencies(
        WbsStatus target, DependencyType type, WbsStatus predStatus, bool isBlocker)
    {
        WbsNode pred = Node(predStatus), succ = Node();
        NodeDependency dep = Dep(pred, succ, type);

        IReadOnlyList<NodeDependency> blockers = DependencyConstraints.TransitionBlockers(
            target, [dep], new Dictionary<Guid, WbsStatus> { [pred.Id] = predStatus });

        Assert.Equal(isBlocker, blockers.Contains(dep));
    }

    [Fact]
    public void TransitionBlockers_SkipsPredecessorsMissingFromStatusMap()
    {
        NodeDependency dep = Dep(Node(), Node());

        Assert.Empty(DependencyConstraints.TransitionBlockers(Complete, [dep], new Dictionary<Guid, WbsStatus>()));
    }
}
