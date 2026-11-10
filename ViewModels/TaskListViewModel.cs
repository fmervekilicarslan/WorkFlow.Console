using WorkFlow.Mvc.Models;

namespace WorkFlow.Mvc.ViewModels;

public class TaskListViewModel
{
    public required string ProjectName { get; init; }
    public required List<TaskItem> Tasks { get; init; }
    public TaskFilterViewModel Filter { get; init; } = new();
    public TaskSummary Summary { get; init; } = new();
    public DateOnly Today { get; init; }
    public TaskPage Pagination { get; init; } = new();
}
