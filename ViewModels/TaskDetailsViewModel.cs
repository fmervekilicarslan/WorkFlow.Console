using WorkFlow.Mvc.Models;

namespace WorkFlow.Mvc.ViewModels;

public sealed class TaskDetailsViewModel
{
    public required TaskItem Task { get; init; }
    public required DateOnly Today { get; init; }
}
