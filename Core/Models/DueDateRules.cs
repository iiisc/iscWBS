namespace iscWBS.Core.Models;

public static class DueDateRules
{
    public const int AtRiskWindowDays = 14;

    public static bool IsOverdue(DateTime? dueDate, WbsStatus status)
        => status != WbsStatus.Complete
           && dueDate.HasValue
           && dueDate.Value.Date < DateTime.Today;

    public static bool IsAtRisk(DateTime? dueDate, WbsStatus status)
        => status != WbsStatus.Complete
           && dueDate.HasValue
           && dueDate.Value.Date >= DateTime.Today
           && dueDate.Value.Date <= DateTime.Today.AddDays(AtRiskWindowDays);
}
