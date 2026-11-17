using Microsoft.EntityFrameworkCore;
using WorkFlow.Mvc.Data;
using WorkFlow.Mvc.Enums;
using WorkFlow.Mvc.Exceptions;
using WorkFlow.Mvc.Services;

internal static class CalendarDateChecks
{
    public static void Run()
    {
        var count = 0;
        void Check(bool value, string label)
        {
            if (!value) throw new InvalidOperationException(label);
            count++; Console.WriteLine("PASS: Calendar dates " + label);
        }
        foreach (var (date, view, start, end) in new[]
        {
            (new DateOnly(2028, 2, 29), "day", new DateOnly(2028, 2, 29), new DateOnly(2028, 3, 1)),
            (new DateOnly(2028, 2, 29), "week", new DateOnly(2028, 2, 28), new DateOnly(2028, 3, 6)),
            (new DateOnly(2028, 2, 29), "month", new DateOnly(2028, 1, 31), new DateOnly(2028, 3, 6)),
            (new DateOnly(2026, 2, 15), "month", new DateOnly(2026, 1, 26), new DateOnly(2026, 3, 2)),
            (new DateOnly(2026, 11, 1), "week", new DateOnly(2026, 10, 26), new DateOnly(2026, 11, 2)),
            (new DateOnly(2027, 1, 1), "week", new DateOnly(2026, 12, 28), new DateOnly(2027, 1, 4)),
            (new DateOnly(2026, 12, 31), "day", new DateOnly(2026, 12, 31), new DateOnly(2027, 1, 1))
        })
        {
            var range = CalendarDates.Range(date, view);
            Check(range.Start == start && range.EndExclusive == end, view + " exact range: " + date);
            Check(view == "day" || range.Start.DayOfWeek == DayOfWeek.Monday && range.EndExclusive.DayOfWeek == DayOfWeek.Monday,
                view + " Monday alignment: " + date);
        }
        var leap = CalendarDates.Range(new DateOnly(2028, 1, 31), "month");
        Check(leap.NextDate == new DateOnly(2028, 2, 29), "month navigation clamps January 31 to leap February");
        Check(CalendarDates.Range(new DateOnly(2027, 1, 31), "month").NextDate == new DateOnly(2027, 2, 28), "month navigation clamps non-leap February");
        foreach (var view in new[] { "day", "week", "month" })
        {
            Check(CalendarDates.Range(CalendarDates.MinDate, view).PreviousDate is null, view + " minimum disables previous navigation");
            Check(CalendarDates.Range(CalendarDates.MaxDate, view).NextDate is null, view + " maximum disables next navigation");
        }
        var utc = TaskDates.ToUtc(new DateOnly(2028, 2, 29));
        Check(utc == new DateTime(2028, 2, 28, 21, 0, 0, DateTimeKind.Utc), "Istanbul midnight is previous UTC day 21:00");
        Check(TaskDates.LocalDate(utc.AddTicks(-10)) == new DateOnly(2028, 2, 28) && TaskDates.LocalDate(utc) == new DateOnly(2028, 2, 29), "one microsecond distinguishes local midnight boundary");
        using var db = new WorkFlowDbContext(new DbContextOptionsBuilder<WorkFlowDbContext>().UseNpgsql("Host=localhost;Database=not_connected").Options);
        var service = new TaskService(db);
        foreach (var invalid in new Action[]
        {
            () => service.TakvimGorevleriniGetir(DateTime.SpecifyKind(utc, DateTimeKind.Unspecified), utc.AddDays(1)),
            () => service.TakvimGorevleriniGetir(utc, utc),
            () => service.TakvimGorevleriniGetir(utc.AddDays(1), utc),
            () => service.TakvimGorevleriniGetir(utc, utc.AddDays(1), status: (Status)999),
            () => service.TarihsizGorevleriGetir(priority: (Priority)999),
            () => service.TarihsizGorevleriGetir(page: 0),
            () => service.TarihsizGorevleriGetir(search: new string('x', 201))
        })
        {
            var rejected = false;
            try { invalid(); } catch (GecersizGorevException) { rejected = true; }
            Check(rejected, "invalid range/filter rejected before database access");
        }
        Console.WriteLine($"{count} calendar date/guard checks passed. Database was not contacted.");
    }
}
