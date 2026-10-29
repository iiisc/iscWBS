namespace iscWBS.Helpers;

/// <summary>
/// Start and due dates are calendar dates, not instants: they are stored as the local date at midnight
/// (<c>DateTimeOffset.Date</c> of the picker value) and compared against <see cref="DateTime.Today"/>.
/// Converting through UTC would shift the stored day for users east of Greenwich.
/// </summary>
public static class CalendarDates
{
    public static DateTimeOffset? ToPickerValue(DateTime? date)
        => date.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(date.Value.Date, DateTimeKind.Local)) : null;
}
