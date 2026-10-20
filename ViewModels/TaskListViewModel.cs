using WorkFlow.Mvc.Models;

namespace WorkFlow.Mvc.ViewModels;

public class TaskListViewModel
{
    public required string ProjectName { get; init; }
    public required List<TaskItem> Tasks { get; init; }
}
