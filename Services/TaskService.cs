using Microsoft.EntityFrameworkCore;
using WorkFlow.Mvc.Data;
using WorkFlow.Mvc.Enums;
using WorkFlow.Mvc.Exceptions;
using WorkFlow.Mvc.Interfaces;
using WorkFlow.Mvc.Models;

namespace WorkFlow.Mvc.Services;

public class TaskService : ITaskService
{
    private readonly WorkFlowDbContext _dbContext;

    public TaskService(WorkFlowDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public void GorevEkle(TaskItem yeniGorev)
    {
        AlanlariDogrula(yeniGorev.Title, yeniGorev.Description, yeniGorev.Priority, yeniGorev.Status, yeniGorev.DueDate);
        yeniGorev.Title = yeniGorev.Title.Trim();
        yeniGorev.Description = yeniGorev.Description?.Trim() ?? string.Empty;
        _dbContext.Tasks.Add(yeniGorev);
        _dbContext.SaveChanges();
    }

    private static void AlanlariDogrula(string title, string? description, Priority priority, Status status, DateTime? dueDate)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new GecersizGorevException("Görev başlığı boş olamaz!");
        }

        if (title.Length > 120 || description?.Length > 1000 ||
            !Enum.IsDefined(priority) || !Enum.IsDefined(status))
            throw new GecersizGorevException("Görev alanlarını kontrol edin.");
        if (dueDate.HasValue && dueDate.Value.Kind != DateTimeKind.Utc)
            throw new GecersizGorevException("Son teslim tarihi UTC olmalıdır.");

        if (dueDate.HasValue && TaskDates.LocalDate(dueDate.Value).Year is < 1900 or > 9998)
            throw new GecersizGorevException("1900 ile 9998 yılları arasında bir tarih seçin.");
    }

    public TaskItem? GorevGetir(Guid id) => _dbContext.Tasks.AsNoTracking().SingleOrDefault(task => task.Id == id);

    public bool GorevGuncelle(Guid id, string title, string? description, Priority priority, Status status, DateTime? dueDate)
    {
        AlanlariDogrula(title, description, priority, status, dueDate);
        var task = _dbContext.Tasks.Find(id);
        if (task is null) return false;
        task.Title = title.Trim();
        task.Description = description?.Trim() ?? string.Empty;
        task.Priority = priority;
        task.Status = status;
        task.DueDate = dueDate;
        try { _dbContext.SaveChanges(); }
        catch (DbUpdateConcurrencyException) { return false; }
        return true;
    }

    public TaskPage SayfaliGorevleriGetir(string? search = null, Status? status = null, Priority? priority = null,
        TaskSort sort = TaskSort.Newest, int page = 1, int pageSize = 10)
    {
        if (page < 1 || pageSize is < 5 or > 50 || !Enum.IsDefined(sort) ||
            (status.HasValue && !Enum.IsDefined(status.Value)) || (priority.HasValue && !Enum.IsDefined(priority.Value)))
            throw new GecersizGorevException("Listeleme seçeneklerini kontrol edin.");
        var query = ListeSorgusu(search, status, priority);
        var count = query.Count();
        page = Math.Min(page, Math.Max(1, (int)Math.Ceiling(count / (double)pageSize)));
        IOrderedQueryable<TaskItem> ordered = sort switch
        {
            TaskSort.Oldest => query.OrderBy(t => t.CreatedDate).ThenBy(t => t.Id),
            TaskSort.PriorityHigh => query.OrderByDescending(t => t.Priority == Priority.Critical ? 3 : t.Priority == Priority.High ? 2 : t.Priority == Priority.Normal ? 1 : 0).ThenByDescending(t => t.CreatedDate).ThenBy(t => t.Id),
            TaskSort.PriorityLow => query.OrderBy(t => t.Priority == Priority.Critical ? 3 : t.Priority == Priority.High ? 2 : t.Priority == Priority.Normal ? 1 : 0).ThenByDescending(t => t.CreatedDate).ThenBy(t => t.Id),
            TaskSort.DueSoon => query.OrderBy(t => t.DueDate == null).ThenBy(t => t.DueDate).ThenByDescending(t => t.CreatedDate).ThenBy(t => t.Id),
            TaskSort.DueLatest => query.OrderBy(t => t.DueDate == null).ThenByDescending(t => t.DueDate).ThenByDescending(t => t.CreatedDate).ThenBy(t => t.Id),
            _ => query.OrderByDescending(t => t.CreatedDate).ThenBy(t => t.Id)
        };
        return new TaskPage { Items = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
            TotalCount = count, Page = page, PageSize = pageSize };
    }

    public void GorevSil(Guid gorevId)
    {
        TaskItem? gorev = _dbContext.Tasks.Find(gorevId);
        if (gorev is null)
        {
            throw new GecersizGorevException("Silinecek görev bulunamadı.");
        }

        _dbContext.Tasks.Remove(gorev);
        _dbContext.SaveChanges();
    }

    public void GorevDurumunuGuncelle(Guid gorevId, Status yeniDurum)
    {
        if (!Enum.IsDefined(yeniDurum))
        {
            throw new GecersizGorevException("Geçersiz görev durumu.");
        }

        TaskItem? gorev = _dbContext.Tasks.Find(gorevId);
        if (gorev is null)
        {
            throw new GecersizGorevException("Güncellenecek görev bulunamadı.");
        }

        gorev.Status = yeniDurum;
        _dbContext.SaveChanges();
    }

    public List<TaskItem> TumGorevleriGetir(string? search = null, Status? status = null, Priority? priority = null)
    {
        return ListeSorgusu(search, status, priority).ToList();
    }

    public IQueryable<TaskItem> ListeSorgusu(string? search = null, Status? status = null, Priority? priority = null)
    {
        var query = _dbContext.Tasks.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = "%" + search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(task => EF.Functions.ILike(task.Title, pattern, "\\") ||
                EF.Functions.ILike(task.Description, pattern, "\\"));
        }
        if (status.HasValue) query = query.Where(task => task.Status == status.Value);
        if (priority.HasValue) query = query.Where(task => task.Priority == priority.Value);
        return query.OrderByDescending(gorev => gorev.CreatedDate)
            .ThenBy(gorev => gorev.Id);
    }

    public TaskSummary OzetGetir() => _dbContext.Tasks.AsNoTracking().GroupBy(task => 1)
        .Select(group => new TaskSummary
        {
            Total = group.Count(),
            Todo = group.Count(task => task.Status == Status.Todo),
            InProgress = group.Count(task => task.Status == Status.InProgress),
            Completed = group.Count(task => task.Status == Status.Completed)
        }).SingleOrDefault() ?? new TaskSummary();
}
