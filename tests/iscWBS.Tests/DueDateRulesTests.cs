using iscWBS.Core.Models;

namespace iscWBS.Tests;

public class DueDateRulesTests
{
    private static readonly DateTime Today = DateTime.Today;

    [Theory]
    [InlineData(-1, true)]
    [InlineData(-30, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public void IsOverdue_WhenDueBeforeToday(int dueInDays, bool expected)
        => Assert.Equal(expected, DueDateRules.IsOverdue(Today.AddDays(dueInDays), WbsStatus.InProgress));

    [Fact]
    public void IsOverdue_IgnoresTimeOfDayOnTheDueDate()
        => Assert.False(DueDateRules.IsOverdue(Today.AddHours(23), WbsStatus.NotStarted));

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(DueDateRules.AtRiskWindowDays, true)]
    [InlineData(DueDateRules.AtRiskWindowDays + 1, false)]
    public void IsAtRisk_WhenDueWithinWindowFromToday(int dueInDays, bool expected)
        => Assert.Equal(expected, DueDateRules.IsAtRisk(Today.AddDays(dueInDays), WbsStatus.NotStarted));

    [Theory]
    [InlineData(-5)]
    [InlineData(3)]
    public void CompleteNodes_AreNeverOverdueOrAtRisk(int dueInDays)
    {
        Assert.False(DueDateRules.IsOverdue(Today.AddDays(dueInDays), WbsStatus.Complete));
        Assert.False(DueDateRules.IsAtRisk(Today.AddDays(dueInDays), WbsStatus.Complete));
    }

    [Fact]
    public void NoDueDate_IsNeitherOverdueNorAtRisk()
    {
        Assert.False(DueDateRules.IsOverdue(null, WbsStatus.NotStarted));
        Assert.False(DueDateRules.IsAtRisk(null, WbsStatus.NotStarted));
    }
}
