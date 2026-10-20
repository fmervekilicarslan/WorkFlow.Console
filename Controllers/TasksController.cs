using Microsoft.AspNetCore.Mvc;
using WorkFlow.Mvc.Enums;
using WorkFlow.Mvc.Exceptions;
using WorkFlow.Mvc.Interfaces;
using WorkFlow.Mvc.Models;
using WorkFlow.Mvc.ViewModels;

namespace WorkFlow.Mvc.Controllers;

public class TasksController : Controller
{
    private readonly ITaskService _taskService;
    private readonly Project _project;

    public TasksController(ITaskService taskService, Project project)
    {
        _taskService = taskService;
        _project = project;
    }

    public IActionResult Index()
    {
        return View(new TaskListViewModel
        {
            ProjectName = _project.Name,
            Tasks = _taskService.TumGorevleriGetir()
        });
    }

    [HttpGet]
    public IActionResult Create() => View(new TaskCreateViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Create(TaskCreateViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Title))
        {
            ModelState.AddModelError(nameof(model.Title), "Başlık boş olamaz.");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            var task = new TaskItem(model.Title.Trim(), model.Description?.Trim() ?? string.Empty,
                model.Priority, model.Status);
            _taskService.GorevEkle(task);
            TempData["Success"] = "Görev eklendi.";
            return RedirectToAction(nameof(Index));
        }
        catch (GecersizGorevException ex)
        {
            ModelState.AddModelError(nameof(model.Title), ex.Message);
            return View(model);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Delete(Guid gorevId)
    {
        try
        {
            _taskService.GorevSil(gorevId);
            TempData["Success"] = "Görev silindi.";
        }
        catch (GecersizGorevException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult UpdateStatus(Guid gorevId, Status yeniDurum)
    {
        try
        {
            _taskService.GorevDurumunuGuncelle(gorevId, yeniDurum);
            TempData["Success"] = "Görev durumu güncellendi.";
        }
        catch (GecersizGorevException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View();
}
