using WorkFlow.Mvc.Enums;
using WorkFlow.Mvc.Exceptions;
using WorkFlow.Mvc.Interfaces;
using WorkFlow.Mvc.Models;

namespace WorkFlow.Mvc.Services;

public class TaskService : ITaskService
{
    private readonly Project _aktifProje;
    private readonly object _kilit = new();

    public TaskService(Project proje)
    {
        _aktifProje = proje;
    }

    public void GorevEkle(TaskItem yeniGorev)
    {
        if (string.IsNullOrWhiteSpace(yeniGorev.Title))
        {
            throw new GecersizGorevException("Görev başlığı boş olamaz!");
        }

        lock (_kilit)
        {
            _aktifProje.ProjectTasks.Add(yeniGorev.Id, yeniGorev);
        }
    }

    public void GorevSil(Guid gorevId)
    {
        lock (_kilit)
        {
            if (!_aktifProje.ProjectTasks.Remove(gorevId))
            {
                throw new GecersizGorevException("Silinecek görev bulunamadı.");
            }
        }
    }

    public void GorevDurumunuGuncelle(Guid gorevId, Status yeniDurum)
    {
        if (!Enum.IsDefined(yeniDurum))
        {
            throw new GecersizGorevException("Geçersiz görev durumu.");
        }

        lock (_kilit)
        {
            if (!_aktifProje.ProjectTasks.TryGetValue(gorevId, out TaskItem? gorev))
            {
                throw new GecersizGorevException("Güncellenecek görev bulunamadı.");
            }

            gorev.Status = yeniDurum;
        }
    }

    public List<TaskItem> TumGorevleriGetir()
    {
        lock (_kilit)
        {
            return _aktifProje.ProjectTasks.Values.ToList();
        }
    }
}
