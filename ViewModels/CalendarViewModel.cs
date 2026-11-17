using WorkFlow.Mvc.Models;

namespace WorkFlow.Mvc.ViewModels;

public sealed class CalendarViewModel
{
    public required string ProjectName { get; init; }
    public string ViewMode { get; init; } = "month";
    public DateOnly SelectedDate { get; init; }
    public DateOnly Today { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly EndDateExclusive { get; init; }
    public DateOnly? PreviousDate { get; init; }
    public DateOnly? NextDate { get; init; }
    public List<CalendarDayViewModel> Days { get; init; } = [];
    public CalendarFilterViewModel Filter { get; init; } = new();
    public TaskPage UndatedTasks { get; init; } = new();
}
