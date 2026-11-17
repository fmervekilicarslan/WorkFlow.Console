using WorkFlow.Mvc.Models;

namespace WorkFlow.Mvc.ViewModels;

public sealed class CalendarDayViewModel
{
    public DateOnly Date { get; init; }
    public List<TaskItem> Tasks { get; init; } = [];
}
