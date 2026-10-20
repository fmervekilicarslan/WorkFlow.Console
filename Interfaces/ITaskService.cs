using WorkFlow.Mvc.Enums;
using WorkFlow.Mvc.Models;

namespace WorkFlow.Mvc.Interfaces;

public interface ITaskService
{
    void GorevEkle(TaskItem yeniGorev);
    void GorevSil(Guid gorevId);
    void GorevDurumunuGuncelle(Guid gorevId, Status yeniDurum);
    List<TaskItem> TumGorevleriGetir();
}
