using WorkFlow.Mvc.Enums;

namespace WorkFlow.Mvc.Services;

public static class TaskDates
{
    public static TimeZoneInfo Istanbul { get; } = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    public static DateTime ToUtc(DateOnly date) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), Istanbul);

    public static DateOnly LocalDate(DateTime utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, Istanbul));

    public static bool IsOverdue(DateTime? dueDate, Status status, DateOnly today) =>
        status != Status.Completed && dueDate.HasValue && LocalDate(dueDate.Value) < today;

    public static bool IsUpcoming(DateTime? dueDate, Status status, DateOnly today) =>
        status != Status.Completed && dueDate.HasValue &&
        LocalDate(dueDate.Value).DayNumber - today.DayNumber is >= 0 and <= 7;
}
