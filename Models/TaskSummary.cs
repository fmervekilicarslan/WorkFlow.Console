namespace WorkFlow.Mvc.Models;

public sealed class TaskSummary
{
    public int Total { get; init; }
    public int Todo { get; init; }
    public int InProgress { get; init; }
    public int Completed { get; init; }
}
