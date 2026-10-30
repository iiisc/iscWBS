using iscWBS.Core.Models;
using static iscWBS.Tests.TestData;

namespace iscWBS.Tests;

public class WorkloadRowTests
{
    private static WbsNode Task(string person, double estimated, double actual, WbsStatus status = WbsStatus.NotStarted, WbsNode? parent = null)
    {
        WbsNode node = Node(status, parent);
        (node.AssignedTo, node.EstimatedHours, node.ActualHours) = (person, estimated, actual);
        return node;
    }

    [Fact]
    public void OpenWorkIsSummedPerPerson_FromTasksWithoutChildren_TheBusiestFirst_UnassignedLast()
    {
        WbsNode parent = Task("Kim", 100, 0);
        WbsNode[] nodes =
        [
            parent,
            Task("Kim", 10, 4, parent: parent),
            Task("kim ", 5, 0, WbsStatus.InProgress, parent),
            Task("Kim", 40, 40, WbsStatus.Complete, parent),
            Task("Sam", 30, 0),
            Task("Sam", 2, 5),
            Task("", 8, 0),
        ];

        List<WorkloadRow> rows = WorkloadRow.Build(nodes, nodes);

        Assert.Equal([("Sam", 2, 30.0), ("Kim", 2, 11.0), ("", 1, 8.0)], rows.Select(r => (r.Person, r.OpenTasks, r.RemainingHours)));
    }
}
