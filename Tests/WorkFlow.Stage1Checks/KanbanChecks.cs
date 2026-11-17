using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WorkFlow.Mvc.Controllers;
using WorkFlow.Mvc.Data;
using WorkFlow.Mvc.Enums;
using WorkFlow.Mvc.Exceptions;
using WorkFlow.Mvc.Interfaces;
using WorkFlow.Mvc.Models;

internal static class KanbanChecks
{
    public static async Task<int> Run(HttpClient client, Guid taskId, Func<WorkFlowDbContext> open)
    {
        var count = 0;
        void Check(bool ok, string label)
        {
            if (!ok) throw new InvalidOperationException(label);
            Console.WriteLine("PASS: Kanban " + label);
            count++;
        }
        string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        async Task<string> Snapshot()
        {
            using var db = open();
            return JsonSerializer.Serialize(await db.Tasks.AsNoTracking().OrderBy(t => t.Id).ToListAsync());
        }
        var token = Token(await client.GetStringAsync("/Tasks"));
        Dictionary<string, string> Fields() => new()
        {
            ["gorevId"] = taskId.ToString(), ["yeniDurum"] = "0", ["__RequestVerificationToken"] = token
        };
        async Task<HttpResponseMessage> Post(Dictionary<string, string> fields) =>
            await client.PostAsync("/Tasks/UpdateBoardStatus", new FormUrlEncodedContent(fields));
        foreach (var status in Enum.GetValues<Status>())
        {
            using var beforeDb = open();
            var before = await beforeDb.Tasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
            var fields = Fields(); fields["yeniDurum"] = ((int)status).ToString();
            using var response = await Post(fields);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Check(response.StatusCode == HttpStatusCode.OK && json.RootElement.GetProperty("success").GetBoolean()
                && json.RootElement.GetProperty("taskId").GetGuid() == taskId
                && json.RootElement.GetProperty("status").GetInt32() == (int)status, "JSON success contract: " + status);
            using var savedDb = open();
            var saved = await savedDb.Tasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
            Check(saved.Status == status && saved.CodeNumber == before.CodeNumber && saved.Id == before.Id
                && saved.CreatedDate == before.CreatedDate && saved.Title == before.Title
                && saved.Description == before.Description && saved.Priority == before.Priority && saved.DueDate == before.DueDate,
                "fresh PostgreSQL read persists only status: " + status);
            var refreshed = await client.GetStringAsync("/Tasks?view=board&Filter.Search=" + Uri.EscapeDataString(saved.Title));
            var refreshedList = await client.GetStringAsync("/Tasks?view=list&Filter.Search=" + Uri.EscapeDataString(saved.Title));
            var card = Regex.Match(refreshed, $"<article\\b[^>]*data-task-id=\"{taskId}\"[^>]*data-status=\"{(int)status}\"[^>]*>");
            var column = Regex.Match(refreshed, $"<section\\b[^>]*data-board-column=\"{(int)status}\"[^>]*>(.*?)</section>", RegexOptions.Singleline);
            var row = Regex.Match(refreshedList, $"<tr\\b[^>]*data-list-task=\"{taskId}\"[^>]*>(.*?)</tr>", RegexOptions.Singleline);
            Check(card.Success && column.Value.Contains(card.Value) && row.Success
                && Regex.IsMatch(row.Value, $"<option[^>]*(?:selected=\"selected\"[^>]*value=\"{status}\"|value=\"{status}\"[^>]*selected=\"selected\")"),
                "reload retains updated card column and selected list status: " + status);
        }
        var invalidCases = new (string Field, string? Value)[]
        {
            ("yeniDurum", null), ("yeniDurum", ""), ("yeniDurum", "999"), ("yeniDurum", "-1"), ("yeniDurum", "abc"),
            ("gorevId", null), ("gorevId", ""), ("gorevId", "abc"), ("gorevId", Guid.Empty.ToString()),
            ("__RequestVerificationToken", null), ("__RequestVerificationToken", "invalid")
        };
        foreach (var (field, value) in invalidCases)
        {
            var before = await Snapshot();
            var fields = Fields();
            if (value is null) fields.Remove(field); else fields[field] = value;
            using var response = await Post(fields);
            Check(response.StatusCode == HttpStatusCode.BadRequest, $"rejects invalid/missing {field} '{value}'");
            Check(await Snapshot() == before, "invalid request leaves every task unchanged");
            if (field != "__RequestVerificationToken")
            {
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Check(!json.RootElement.GetProperty("success").GetBoolean()
                    && json.RootElement.GetProperty("message").GetString() == "Geçerli bir görev ve durum seçin.", "invalid request has Turkish JSON message");
            }
        }
        var missingFields = Fields(); missingFields["gorevId"] = Guid.NewGuid().ToString();
        var missingBefore = await Snapshot();
        using (var missing = await Post(missingFields))
        {
            using var json = JsonDocument.Parse(await missing.Content.ReadAsStringAsync());
            Check(missing.StatusCode == HttpStatusCode.NotFound && !json.RootElement.GetProperty("success").GetBoolean(), "missing task returns JSON 404");
            Check(await Snapshot() == missingBefore, "missing task leaves every task unchanged");
        }
        using (var get = await client.GetAsync("/Tasks/UpdateBoardStatus"))
            Check(get.StatusCode == HttpStatusCode.MethodNotAllowed, "GET cannot mutate board status");
        var filtered = await client.GetStringAsync("/Tasks?view=list&Filter.PageSize=50");
        var fullBoard = await client.GetStringAsync("/Tasks?view=board&Filter.Status=0&Filter.PageSize=5&Filter.Page=2");
        var listIds = Regex.Matches(filtered, "data-list-task=\"([^\"]+)\"").Select(m => m.Groups[1].Value).Order().ToArray();
        var boardIds = Regex.Matches(fullBoard, "data-board-card[^>]*data-task-id=\"([^\"]+)\"").Select(m => m.Groups[1].Value).Order().ToArray();
        Check(listIds.Length > 10 && listIds.SequenceEqual(boardIds), "complete list and unpaginated board contain identical task IDs across separate responses");
        count += await BoardQueryChecks.Run(client, taskId, open);

        foreach (var (failure, expected) in new (Exception Failure, int Status)[]
        {
            (new DbUpdateConcurrencyException("private-provider-detail"), 409),
            (new DbUpdateException("private-provider-detail"), 503),
            (new NpgsqlException("private-provider-detail"), 503),
            (new GecersizGorevException("private-provider-detail"), 404)
        })
        {
            var fake = new FailingService(failure);
            var controller = new TasksController(fake, new Project("Kanban test"));
            var result = controller.UpdateBoardStatus(fake.Task.Id, Status.Completed) as ObjectResult;
            var json = JsonSerializer.Serialize(result?.Value);
            Check(result?.StatusCode == expected && json.Contains("\"success\":false") && !json.Contains("private-provider-detail"),
                "safe failure response: " + failure.GetType().Name);
            Check(fake.Task.Status == Status.Todo, "failed service update preserves task state");
        }
        return count;
    }

    private sealed class FailingService(Exception failure) : ITaskService
    {
        public TaskItem Task { get; } = new("Kanban failure test", "", Priority.Normal, Status.Todo);
        public TaskItem? GorevGetir(Guid id) => Task;
        public void GorevDurumunuGuncelle(Guid id, Status status) => throw failure;
        public void GorevEkle(TaskItem task) => throw new NotSupportedException();
        public void GorevSil(Guid id) => throw new NotSupportedException();
        public List<TaskItem> TumGorevleriGetir(string? search = null, Status? status = null, Priority? priority = null) => throw new NotSupportedException();
        public TaskSummary OzetGetir() => throw new NotSupportedException();
        public List<TaskItem> TakvimGorevleriniGetir(DateTime startUtc, DateTime endUtc, string? search = null,
            Status? status = null, Priority? priority = null) => throw new NotSupportedException();
        public TaskPage TarihsizGorevleriGetir(string? search = null, Status? status = null, Priority? priority = null,
            int page = 1, int pageSize = 10) => throw new NotSupportedException();
        public bool GorevGuncelle(Guid id, string title, string? description, Priority priority, Status status, DateTime? dueDate) => throw new NotSupportedException();
        public TaskPage SayfaliGorevleriGetir(string? search = null, Status? status = null, Priority? priority = null,
            TaskSort sort = TaskSort.Newest, int page = 1, int pageSize = 10) => throw new NotSupportedException();
    }
}
