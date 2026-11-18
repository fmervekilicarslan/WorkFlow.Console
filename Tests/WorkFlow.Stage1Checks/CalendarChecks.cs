using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using WorkFlow.Mvc.Controllers;
using WorkFlow.Mvc.Data;
using WorkFlow.Mvc.Enums;
using WorkFlow.Mvc.Models;
using WorkFlow.Mvc.Services;
using WorkFlow.Mvc.ViewModels;

internal static class CalendarChecks
{
    public static async Task<int> Run(HttpClient client, Func<WorkFlowDbContext> open, TestAccount account, TestAuthHost host, TestSession session)
    {
        var count = 0;
        void Check(bool ok, string label)
        {
            if (!ok) throw new InvalidOperationException(label);
            count++; Console.WriteLine("PASS: Calendar " + label);
        }
        async Task<string> Snapshot()
        {
            using var db = open();
            return JsonSerializer.Serialize(await db.Tasks.AsNoTracking().OrderBy(t => t.Id).ToListAsync());
        }
        var before = await Snapshot();
        var marker = "CALENDAR-" + Guid.NewGuid().ToString("N");
        var date = new DateOnly(2028, 2, 29);
        var start = TaskDates.ToUtc(date);
        var end = TaskDates.ToUtc(date.AddDays(1));
        var today = TaskDates.LocalDate(DateTime.UtcNow);
        var fixtures = new List<TaskItem>();
        TaskItem Add(string title, DateTime? due, Status status = Status.Todo, Priority priority = Priority.Normal)
        {
            var task = new TaskItem(marker + " " + title, "Calendar fixture <safe>", priority, status) { DueDate = due };
            task.AssignOwner(account.UserId);
            fixtures.Add(task); return task;
        }
        var beforeStart = Add("before-start", start.AddTicks(-10));
        var atStart = Add("at-start", start, Status.InProgress, Priority.Critical);
        var beforeEnd = Add("before-end", end.AddTicks(-10), Status.Completed, Priority.High);
        var atEnd = Add("at-end", end);
        for (var i = 0; i < 8; i++) Add(i == 0 ? "busy-long-" + new string('ğ', 120 - marker.Length - 11) : "busy-" + i, start.AddHours(4), (Status)(i % 3), (Priority)(i % 4));
        for (var i = 0; i < 12; i++) Add("undated-" + i, null, (Status)(i % 3), (Priority)(i % 4));
        var overdue = Add("overdue", TaskDates.ToUtc(today.AddDays(-1)));
        var upcoming = Add("upcoming", TaskDates.ToUtc(today.AddDays(1)));
        var completed = Add("completed", TaskDates.ToUtc(today.AddDays(-1)), Status.Completed);
        try
        {
            using var setup = open();
            setup.Tasks.AddRange(fixtures); await setup.SaveChangesAsync();
            var service = new TaskService(setup, account);
            var range = service.TakvimGorevleriniGetir(start, end, marker);
            Check(range.Count == 10 && range.Any(t => t.Id == atStart.Id) && range.Any(t => t.Id == beforeEnd.Id)
                && range.All(t => t.Id != beforeStart.Id && t.Id != atEnd.Id), "half-open UTC range includes midnight and excludes adjacent microseconds");
            Check(range.All(t => t.DueDate.HasValue) && range.Select(t => t.DueDate).SequenceEqual(range.Select(t => t.DueDate).Order()), "dated query excludes undated tasks and orders due dates");
            var undated1 = service.TarihsizGorevleriGetir(marker);
            var undated2 = service.TarihsizGorevleriGetir(marker, page: 2);
            Check(undated1.TotalCount == 12 && undated1.Items.Count == 10 && undated2.Items.Count == 2 && undated2.Page == 2, "undated tasks have separate count and pagination");
            Check(!undated1.Items.Select(t => t.Id).Intersect(undated2.Items.Select(t => t.Id)).Any() && undated1.Items.Concat(undated2.Items).All(t => t.DueDate is null), "undated pages do not overlap or contain dated tasks");
            foreach (var status in Enum.GetValues<Status>())
            {
                var expected = fixtures.Where(t => t.DueDate >= start && t.DueDate < end && t.Status == status).Select(t => t.Id).Order();
                Check(service.TakvimGorevleriniGetir(start, end, marker, status).Select(t => t.Id).Order().SequenceEqual(expected), "range status filter: " + status);
            }
            foreach (var priority in Enum.GetValues<Priority>())
                Check(service.TakvimGorevleriniGetir(start, end, marker, priority: priority).All(t => t.Priority == priority), "range priority filter: " + priority);
            Check(service.TakvimGorevleriniGetir(start, end, atStart.Title, Status.InProgress, Priority.Critical).Single().Id == atStart.Id, "search, status and priority combine");
            var commands = new CalendarCommandCounter();
            using (var db = new WorkFlowDbContext(new DbContextOptionsBuilder<WorkFlowDbContext>()
                .UseNpgsql(setup.Database.GetConnectionString()).AddInterceptors(commands).Options))
            {
                var controller = new CalendarController(new TaskService(db, account), new Project("QA"));
                var model = (CalendarViewModel)((ViewResult)controller.Index(new CalendarFilterViewModel { Search = marker }, "day", "2028-02-29")).Model!;
                Check(commands.Sql.Count == 3 && model.Days.Count == 1 && model.Days[0].Tasks.Count == 10, "controller executes range plus undated count/page queries");
                Check(!db.ChangeTracker.Entries<TaskItem>().Any(), "all calendar query results are AsNoTracking");
                Check(commands.Sql[0].Contains("\"DueDate\" >=") && commands.Sql[0].Contains("\"DueDate\" <")
                    && !commands.Sql[0].Contains("date_trunc") && !commands.Sql[0].Contains("LIMIT"), "SQL filters range server-side without date conversion or pagination");
                Check(commands.Values.Contains(start) && commands.Values.Contains(end) && commands.Values.OfType<DateTime>().All(d => d.Kind == DateTimeKind.Utc), "range SQL parameters retain exact UTC boundaries");
            }
            var saved = await Snapshot();
            var search = Uri.EscapeDataString(marker);
            async Task<string> Get(string query) => WebUtility.HtmlDecode(await client.GetStringAsync("/Calendar?Filter.Search=" + search + "&" + query));
            var day = await Get("view=day&date=2028-02-29");
            Check(Regex.Matches(day, "data-calendar-day=").Count == 1 && day.Contains(atStart.Code) && day.Contains(beforeEnd.Code)
                && !day.Contains(beforeStart.Code) && !day.Contains(atEnd.Code), "day HTML reflects exact Istanbul date boundary");
            var week = await Get("view=week&date=2028-02-29");
            Check(Regex.Matches(week, "data-calendar-day=").Count == 7 && week.Contains("data-calendar-day=\"2028-02-28\"") && week.Contains("data-calendar-day=\"2028-03-05\""), "week renders Monday through Sunday across month boundary");
            var month = await Get("view=month&date=2028-02-29");
            Check(Regex.Matches(month, "data-calendar-day=").Count == 35 && month.Contains("data-calendar-day=\"2028-01-31\"") && month.Contains("data-calendar-day=\"2028-03-05\""), "leap month includes Monday-aligned padded dates");
            Check(month.Contains("class=\"pen-calendar-more\"") && month.Contains("+7 görev daha"), "busy month day exposes correct additional-task disclosure");
            foreach (var (view, html) in new[] { ("day", day), ("week", week), ("month", month) })
            {
                var cell = Regex.Match(html, "<section[^>]*data-calendar-day=\"2028-02-29\"[^>]*>.*?</section>", RegexOptions.Singleline).Value;
                Check(cell.Contains(atStart.Id.ToString()) && cell.Contains(beforeEnd.Id.ToString())
                    && !cell.Contains(beforeStart.Id.ToString()) && !cell.Contains(atEnd.Id.ToString()), view + " keeps midnight-boundary tasks inside their exact local date cell");
            }
            var turkishDate = await Get("view=day&date=29.02.2028");
            Check(turkishDate.Contains("data-calendar-day=\"2028-02-29\"") && turkishDate.Contains(atStart.Code)
                && turkishDate.Contains("29.02.2028"), "strict Turkish leap date selects the same Istanbul day as ISO");
            foreach (var invalidDate in new[] { "29.02.2027", "31.04.2026", "11/12/2026", "01.01.1899", "01.01.9999" })
            {
                var rejected = await Get("view=day&date=" + Uri.EscapeDataString(invalidDate));
                Check(rejected.Contains("1900 ile 9998 yılları arasında geçerli bir tarih seçin. Tarihi GG.AA.YYYY biçiminde girin.")
                    && Regex.IsMatch(rejected, "id=\"calendar-date\"[^>]*value=\"" + Regex.Escape(invalidDate) + "\""), "invalid or ambiguous Turkish date rejected and attempted value retained: " + invalidDate);
            }
            Check(day.Contains($"href=\"/Tasks/Details/{atStart.Id}\"") && day.Contains(atStart.Code) && day.Contains("Kritik"), "calendar task includes permanent code, priority and details link");
            Check(day.Contains("Filter.UndatedPage=2") && day.Contains("12 görev"), "undated pagination links and total render separately");
            var undatedPage = await Get("view=day&date=2028-02-29&Filter.UndatedPage=2");
            var undatedSection = Regex.Match(undatedPage, "<section class=\"pen-calendar-undated\".*?</section>", RegexOptions.Singleline).Value;
            Check(Regex.Matches(undatedSection, "class=\"pen-calendar-task\"").Count == 2 && undatedPage.Contains(atStart.Code), "undated second page does not restrict dated day results");
            foreach (var (suffix, message) in new[] { ("view=day&date=2028-02-30", "geçerli bir tarih"), ("view=invalid", "Geçerli bir takvim görünümü"),
                         ("Filter.Status=abc", "Geçerli bir durum"), ("Filter.Priority=999", "Geçerli bir öncelik"), ("Filter.UndatedPage=0", "tarihsiz görev sayfası") })
                Check((await Get(suffix)).Contains(message, StringComparison.OrdinalIgnoreCase), "invalid calendar input has Turkish feedback: " + suffix);
            var empty = await client.GetStringAsync("/Calendar?view=day&date=2028-02-29&Filter.Search=CALENDAR-no-matching-task");
            Check(empty.Contains("pen-calendar-empty") && !empty.Contains("class=\"pen-calendar-task\""), "empty date and undated results render feedback");
            var past = await Get("view=day&date=" + today.AddDays(-1).ToString("yyyy-MM-dd"));
            string Card(string html, Guid id) => Regex.Matches(html, "<article class=\"pen-calendar-task\">.*?</article>", RegexOptions.Singleline).Select(m => m.Value).Single(c => c.Contains(id.ToString()));
            Check(Card(past, overdue.Id).Contains("pen-overdue") && !Card(past, completed.Id).Contains("pen-overdue") && !Card(past, completed.Id).Contains("pen-upcoming"), "completed task suppresses overdue/upcoming markers");
            var soon = await Get("view=day&date=" + today.AddDays(1).ToString("yyyy-MM-dd"));
            Check(Card(soon, upcoming.Id).Contains("pen-upcoming"), "upcoming due date receives upcoming marker");
            using (var post = await client.PostAsync("/Calendar", new FormUrlEncodedContent(new Dictionary<string, string>())))
                Check(post.StatusCode == HttpStatusCode.MethodNotAllowed, "calendar is GET-only");
            if (Environment.GetEnvironmentVariable("WORKFLOW_BROWSER_CHECKS") == "1")
            {
                var startBrowser = new ProcessStartInfo("powershell") { UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
                    Path.Combine(Directory.GetCurrentDirectory(), "Tests", "CalendarBrowserChecks.ps1"), "-BaseUrl", client.BaseAddress!.ToString().TrimEnd('/'),
                    "-Search", marker, "-TaskId", atStart.Id.ToString() }) startBrowser.ArgumentList.Add(argument);
                host.ConfigureBrowser(startBrowser, session);
                using var browser = Process.Start(startBrowser)!;
                var stdout = browser.StandardOutput.ReadToEndAsync(); var stderr = browser.StandardError.ReadToEndAsync();
                await browser.WaitForExitAsync(); var output = await stdout; Console.Write(output);
                if (browser.ExitCode != 0) throw new InvalidOperationException("Calendar browser checks failed: " + await stderr);
                count += Regex.Matches(output, "(?m)^PASS:").Count;
            }
            Check(await Snapshot() == saved, "all calendar GET and browser requests leave every task unchanged");
        }
        finally
        {
            using var cleanup = open();
            await cleanup.Tasks.Where(t => t.Title.StartsWith(marker)).ExecuteDeleteAsync();
        }
        Check(await Snapshot() == before, "owned calendar fixtures cleaned; every original task preserved");
        return count;
    }

    private sealed class CalendarCommandCounter : DbCommandInterceptor
    {
        public List<string> Sql { get; } = [];
        public List<object?> Values { get; } = [];
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Sql.Add(command.CommandText);
            Values.AddRange(command.Parameters.Cast<DbParameter>().Select(p => p.Value)); return result;
        }
    }
}
