using WorkFlow.Mvc.Enums;
using WorkFlow.Mvc.Models;

namespace WorkFlow.Mvc.Interfaces;

public interface ITaskService
{
    void GorevEkle(TaskItem yeniGorev);
    void GorevSil(Guid gorevId);
    void GorevDurumunuGuncelle(Guid gorevId, Status yeniDurum);
    List<TaskItem> TumGorevleriGetir(string? search = null, Status? status = null, Priority? priority = null);
    TaskSummary OzetGetir();
    TaskItem? GorevGetir(Guid id);
    bool GorevGuncelle(Guid id, string title, string? description, Priority priority, Status status, DateTime? dueDate);
    TaskPage SayfaliGorevleriGetir(string? search = null, Status? status = null, Priority? priority = null,
        TaskSort sort = TaskSort.Newest, int page = 1, int pageSize = 10);
}
