namespace WorkFlow.Mvc.Models;

public class Project
{
    public Guid ProjectId { get; private set; }
    public string Name { get; set; }
    public Dictionary<Guid, TaskItem> ProjectTasks { get; set; }

    public Project(string name)
    {
        ProjectId = Guid.NewGuid();
        Name = name;
        ProjectTasks = new Dictionary<Guid, TaskItem>();
    }
}
