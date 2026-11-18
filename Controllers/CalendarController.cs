using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using WorkFlow.Mvc.Interfaces;
using WorkFlow.Mvc.Models;
using WorkFlow.Mvc.Services;
using WorkFlow.Mvc.ViewModels;

namespace WorkFlow.Mvc.Controllers;

public sealed class CalendarController(ITaskService taskService, Project project) : Controller
{
    [HttpGet]
    public IActionResult Index([Bind(Prefix = "Filter")] CalendarFilterViewModel filter,
        string? view = null, string? date = null)
    {
        var today = TaskDates.LocalDate(DateTime.UtcNow);
        var selectedDate = today;
        var mode = string.IsNullOrEmpty(view) ? "month" : view.ToLowerInvariant();
        if (mode is not ("day" or "week" or "month"))
        {
            ModelState.AddModelError(nameof(view), "Geçerli bir takvim görünümü seçin.");
            mode = "month";
        }

        if (date is not null && (!DateOnly.TryParseExact(date, ["yyyy-MM-dd", "dd.MM.yyyy"], CultureInfo.InvariantCulture,
                DateTimeStyles.None, out selectedDate) || selectedDate < CalendarDates.MinDate || selectedDate > CalendarDates.MaxDate))
        {
            ModelState.AddModelError(nameof(date), "1900 ile 9998 yılları arasında geçerli bir tarih seçin. Tarihi GG.AA.YYYY biçiminde girin.");
            selectedDate = today;
        }

        NormalizeFilterErrors();
        var range = CalendarDates.Range(selectedDate, mode);
        List<TaskItem> datedTasks = [];
        var undatedTasks = new TaskPage();
        if (ModelState.IsValid)
        {
            try
            {
                datedTasks = taskService.TakvimGorevleriniGetir(TaskDates.ToUtc(range.Start), TaskDates.ToUtc(range.EndExclusive),
                    filter.Search, filter.Status, filter.Priority);
                undatedTasks = taskService.TarihsizGorevleriGetir(filter.Search, filter.Status, filter.Priority, filter.UndatedPage);
                filter.UndatedPage = undatedTasks.Page;
            }
            catch (NpgsqlException)
            {
                Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                ModelState.AddModelError(string.Empty, "Takvim yüklenemedi. Lütfen tekrar deneyin.");
            }
        }

        var byDate = datedTasks.ToLookup(task => TaskDates.LocalDate(task.DueDate!.Value));
        var days = new List<CalendarDayViewModel>();
        for (var day = range.Start; day < range.EndExclusive; day = day.AddDays(1))
            days.Add(new CalendarDayViewModel { Date = day, Tasks = byDate[day].ToList() });

        return View(new CalendarViewModel
        {
            ProjectName = project.Name, ViewMode = mode, SelectedDate = selectedDate, Today = today,
            StartDate = range.Start, EndDateExclusive = range.EndExclusive,
            PreviousDate = range.PreviousDate, NextDate = range.NextDate,
            Days = days, Filter = filter, UndatedTasks = undatedTasks
        });
    }

    private void NormalizeFilterErrors()
    {
        foreach (var (field, message) in new[]
                 {
                     ("Filter.Status", "Geçerli bir durum seçin."),
                     ("Filter.Priority", "Geçerli bir öncelik seçin."),
                     ("Filter.UndatedPage", "Geçerli bir tarihsiz görev sayfası seçin.")
                 })
        {
            if (ModelState[field] is not { Errors.Count: > 0 } entry) continue;
            entry.Errors.Clear();
            ModelState.AddModelError(field, message);
        }
    }
}
