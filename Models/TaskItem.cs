using WorkFlow.Mvc.Enums;

namespace WorkFlow.Mvc.Models;

public class TaskItem
{
    public Guid Id { get; private set; }
    public string Description { get; set; }
    public string Title { get; set; }
    public Status Status { get; set; }
    public Priority Priority { get; set; }
    public DateTime CreatedDate { get; private set; }
    public DateTime? DueDate { get; set; }

    public TaskItem(string title, string description, Priority priority, Status status)
    {
        Id = Guid.NewGuid();
        Title = title;
        Description = description;
        Priority = priority;
        Status = status;
        CreatedDate = DateTime.Now;
    }
}
