using iscWBS.Core.Models;
using static iscWBS.Tests.TestData;

namespace iscWBS.Tests;

public class SchedulerTests
{
    private static readonly DateTime Day0 = new(2026, 3, 2);

    private static WbsNode Work(int start, int due, WbsStatus status = WbsStatus.NotStarted, WbsNode? parent = null)
    {
        WbsNode node = Node(status, parent);
        node.StartDate = Day0.AddDays(start);
        node.DueDate = Day0.AddDays(due);
        return node;
    }

    private static (int Start, int Due) Days(WbsNode node) => ((node.StartDate!.Value - Day0).Days, (node.DueDate!.Value - Day0).Days);

    [Theory]
    [InlineData(DependencyType.FinishToStart, 10, 15)]   // starts when the predecessor finishes (day 10)
    [InlineData(DependencyType.StartToStart, 4, 9)]      // starts when the predecessor starts (day 4)
    [InlineData(DependencyType.FinishToFinish, 5, 10)]   // finishes when the predecessor finishes (day 10)
    [InlineData(DependencyType.StartToFinish, 0, 5)]     // already finishes after the predecessor starts
    public void Successor_MovesJustFarEnough_KeepingItsDuration(DependencyType type, int start, int due)
    {
        WbsNode predecessor = Work(4, 10);
        WbsNode successor = Work(0, 5);

        Scheduler.PushSuccessors([predecessor, successor], [Dep(predecessor, successor, type)], [predecessor.Id]);

        Assert.Equal((start, due), Days(successor));
    }

    [Theory]
    [InlineData(3, 13, 18)]     // waits three days after the predecessor finishes
    [InlineData(-2, 8, 13)]     // may start two days before it finishes
    public void ADelay_MovesTheSuccessorThatMuchFurther(int lagDays, int start, int due)
    {
        WbsNode predecessor = Work(4, 10);
        WbsNode successor = Work(0, 5);
        NodeDependency dependency = Dep(predecessor, successor);
        dependency.LagDays = lagDays;

        Scheduler.PushSuccessors([predecessor, successor], [dependency], [predecessor.Id]);

        Assert.Equal((start, due), Days(successor));
    }

    [Fact]
    public void Pushes_CascadeDownTheChain_AndReportWhatMoved()
    {
        WbsNode a = Work(0, 10);
        WbsNode b = Work(5, 8);
        WbsNode c = Work(8, 12);

        IReadOnlyList<WbsNode> moved = Scheduler.PushSuccessors([a, b, c], [Dep(a, b), Dep(b, c)], [a.Id]);

        Assert.Equal([b, c], moved);
        Assert.Equal((10, 13), Days(b));
        Assert.Equal((13, 17), Days(c));
    }

    [Fact]
    public void StartedOrFinishedWork_AndWorkWithSlack_StaysPut()
    {
        WbsNode predecessor = Work(0, 10);
        WbsNode started = Work(5, 8, WbsStatus.InProgress);
        WbsNode done = Work(5, 8, WbsStatus.Complete);
        WbsNode later = Work(20, 25);

        IReadOnlyList<WbsNode> moved = Scheduler.PushSuccessors(
            [predecessor, started, done, later], [Dep(predecessor, started), Dep(predecessor, done), Dep(predecessor, later)], [predecessor.Id]);

        Assert.Empty(moved);
        Assert.Equal((20, 25), Days(later));
    }

    [Fact]
    public void CriticalPath_RunsBackFromTheFinishThroughDependenciesWithoutSlack()
    {
        WbsNode parent = Work(0, 20);
        WbsNode a = Work(0, 5, parent: parent);
        WbsNode b = Work(5, 12, parent: parent);    // starts the day a finishes
        WbsNode c = Work(12, 20, parent: parent);   // starts the day b finishes and ends the project
        WbsNode slack = Work(0, 3, parent: parent); // c waits on it, but not because of it
        WbsNode side = Work(0, 9, parent: parent);

        IReadOnlySet<Guid> critical = Scheduler.CriticalPath(
            [parent, a, b, c, slack, side], [Dep(a, b), Dep(b, c), Dep(slack, c)]);

        Assert.Equal(new HashSet<Guid> { a.Id, b.Id, c.Id }, critical);
    }
}
