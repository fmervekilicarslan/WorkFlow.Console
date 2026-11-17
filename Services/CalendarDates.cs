using WorkFlow.Mvc.Models;

namespace WorkFlow.Mvc.Services;

public static class CalendarDates
{
    public static DateOnly MinDate { get; } = new(1900, 1, 1);
    public static DateOnly MaxDate { get; } = new(9998, 12, 31);

    public static CalendarRange Range(DateOnly date, string view)
    {
        if (date < MinDate || date > MaxDate) throw new ArgumentOutOfRangeException(nameof(date));

        DateOnly start;
        DateOnly end;
        DateOnly previous;
        DateOnly next;
        switch (view)
        {
            case "day":
                start = date;
                end = date.AddDays(1);
                previous = date.AddDays(-1);
                next = date.AddDays(1);
                break;
            case "week":
                start = Monday(date);
                end = start.AddDays(7);
                previous = date.AddDays(-7);
                next = date.AddDays(7);
                break;
            case "month":
                var first = new DateOnly(date.Year, date.Month, 1);
                start = Monday(first);
                var nextMonth = first.AddMonths(1);
                end = nextMonth.AddDays((7 - MondayOffset(nextMonth)) % 7);
                previous = date.AddMonths(-1);
                next = date.AddMonths(1);
                break;
            default:
                throw new ArgumentException("Geçerli bir takvim görünümü seçin.", nameof(view));
        }

        return new CalendarRange(start, end, Supported(previous), Supported(next));
    }

    private static int MondayOffset(DateOnly date) => ((int)date.DayOfWeek + 6) % 7;
    private static DateOnly Monday(DateOnly date) => date.AddDays(-MondayOffset(date));
    private static DateOnly? Supported(DateOnly date) => date >= MinDate && date <= MaxDate ? date : null;
}
