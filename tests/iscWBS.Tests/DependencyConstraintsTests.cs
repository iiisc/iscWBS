using iscWBS.Core.Models;
using static iscWBS.Core.Models.DependencyType;
using static iscWBS.Core.Models.WbsStatus;

namespace iscWBS.Tests;

public class DependencyConstraintsTests
{
    [Theory]
    [InlineData(FinishToStart, NotStarted, true, false)]
    [InlineData(FinishToStart, InProgress, true, false)]
    [InlineData(FinishToStart, Complete, false, false)]
    [InlineData(FinishToStart, Blocked, true, false)]
    [InlineData(StartToStart, NotStarted, true, false)]
    [InlineData(StartToStart, InProgress, false, false)]
    [InlineData(StartToStart, Complete, false, false)]
    [InlineData(StartToStart, Blocked, true, false)]
    [InlineData(FinishToFinish, NotStarted, false, true)]
    [InlineData(FinishToFinish, InProgress, false, true)]
    [InlineData(FinishToFinish, Complete, false, false)]
    [InlineData(FinishToFinish, Blocked, false, true)]
    [InlineData(StartToFinish, NotStarted, false, true)]
    [InlineData(StartToFinish, InProgress, false, false)]
    [InlineData(StartToFinish, Complete, false, false)]
    [InlineData(StartToFinish, Blocked, false, true)]
    public void Predicates_MatchPdmSemantics(DependencyType type, WbsStatus predecessor, bool startViolated, bool finishViolated)
    {
        Assert.Equal(startViolated, DependencyConstraints.IsStartViolated(type, predecessor));
        Assert.Equal(finishViolated, DependencyConstraints.IsFinishViolated(type, predecessor));
        Assert.Equal(startViolated || finishViolated, DependencyConstraints.IsViolated(type, predecessor));
    }

    [Theory]
    [InlineData(FinishToStart, false)]
    [InlineData(StartToStart, false)]
    [InlineData(FinishToFinish, true)]
    [InlineData(StartToFinish, true)]
    public void IsFinishConstraint_OnlyForFinishSideTypes(DependencyType type, bool expected)
        => Assert.Equal(expected, DependencyConstraints.IsFinishConstraint(type));
}
