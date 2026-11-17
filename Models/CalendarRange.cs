namespace WorkFlow.Mvc.Models;

public sealed record CalendarRange(DateOnly Start, DateOnly EndExclusive,
    DateOnly? PreviousDate, DateOnly? NextDate);
