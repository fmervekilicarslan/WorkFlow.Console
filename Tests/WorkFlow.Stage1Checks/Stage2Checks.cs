using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WorkFlow.Mvc.Data;
using WorkFlow.Mvc.Enums;
using WorkFlow.Mvc.Exceptions;
using WorkFlow.Mvc.Models;
using WorkFlow.Mvc.Services;

internal static class Stage2Checks
{
    public static async Task Run()
    {
        var connection = await TestDatabase.VerifiedConnection();
        WorkFlowDbContext Open() => new(new DbContextOptionsBuilder<WorkFlowDbContext>().UseNpgsql(connection.ConnectionString).Options);
        await using var account = await TestAccount.Create(Open);
        int count = 0;
        void Check(bool ok, string message)
        { if (!ok) throw new InvalidOperationException(message); Console.WriteLine("PASS: " + message); count++; }
        using var initial = Open();
        var before = JsonSerializer.Serialize(await initial.Tasks.AsNoTracking().OrderBy(t => t.Id).ToListAsync());
        var migrations = (await initial.Database.GetAppliedMigrationsAsync()).ToArray();
        Check(migrations.Length == 4 && migrations.SequenceEqual(initial.Database.GetMigrations()), "Existing original, Identity and profile migrations applied with no pending schema changes");
        var marker = "STAGE2-" + Guid.NewGuid().ToString("N");
        try
        {
            using var db = Open();
            var service = new TaskService(db, account);
            var created = new List<TaskItem>();
            var priorities = new[] { Priority.Low, Priority.Normal, Priority.High, Priority.Critical };
            var today = TaskDates.LocalDate(DateTime.UtcNow);
            for (int i = 0; i < 12; i++)
            {
                var task = new TaskItem(marker + " " + i, marker + " description", priorities[i % 4], Status.Todo)
                { DueDate = i % 4 == 0 ? null : TaskDates.ToUtc(today.AddDays(i)) };
                service.GorevEkle(task);
                db.Entry(task).Property(t => t.CreatedDate).CurrentValue = DateTime.UtcNow.AddMinutes(-100 + i);
                db.SaveChanges();
                created.Add(task);
            }
            var first = service.SayfaliGorevleriGetir(marker, pageSize: 5);
            Check(service.TumGorevleriGetir(marker + " 11").Single().Id == created[11].Id, "Title search finds the expected task");
            Check(service.TumGorevleriGetir(marker + " description").Count == 12, "Description search finds all test tasks");
            var second = service.SayfaliGorevleriGetir(marker, page: 2, pageSize: 5);
            var last = service.SayfaliGorevleriGetir(marker, page: int.MaxValue, pageSize: 5);
            Check(first.TotalCount == 12 && first.TotalPages == 3 && first.Items.Count == 5 && last.Page == 3 && last.Items.Count == 2, "Pagination counts and oversized page clamp");
            Check(!first.Items.Select(t => t.Id).Intersect(second.Items.Select(t => t.Id)).Any(), "Pages have no overlapping rows");
            Check(first.Items[0].Id == created[11].Id, "Default ordering is newest first");
            Check(service.SayfaliGorevleriGetir(marker, sort: TaskSort.Oldest).Items[0].Id == created[0].Id, "Oldest first");
            Check(service.SayfaliGorevleriGetir(marker, sort: TaskSort.PriorityHigh).Items[0].Priority == Priority.Critical, "Critical precedes High despite stored enum values");
            Check(service.SayfaliGorevleriGetir(marker, sort: TaskSort.PriorityLow).Items[0].Priority == Priority.Low, "Low priority first");
            foreach (var sort in new[] { TaskSort.DueSoon, TaskSort.DueLatest })
            {
                var rows = service.SayfaliGorevleriGetir(marker, sort: sort, pageSize: 20).Items;
                var dates = rows.Where(t => t.DueDate.HasValue).Select(t => t.DueDate!.Value).ToArray();
                Check(rows.Take(9).All(t => t.DueDate.HasValue) && rows.Skip(9).All(t => !t.DueDate.HasValue), sort + " keeps undated tasks last");
                Check(dates.SequenceEqual(sort == TaskSort.DueSoon ? dates.OrderBy(d => d) : dates.OrderByDescending(d => d)), sort + " date ordering");
            }
            Check(service.SayfaliGorevleriGetir(marker, Status.Todo, Priority.Critical, pageSize: 5).TotalCount == 3, "Search and both filters feed pagination count");
            var empty = service.SayfaliGorevleriGetir(marker, Status.Completed);
            Check(empty.TotalCount == 0 && empty.Items.Count == 0 && empty.Page == 1, "Empty filtered page");
            var target = created[0];
            // Compare persisted timestamps on both sides: PostgreSQL stores microseconds, .NET ticks are finer.
            var persisted = service.GorevGetir(target.Id)!;
            var identity = (persisted.Id, persisted.CodeNumber, persisted.CreatedDate);
            Check(service.GorevGuncelle(target.Id, target.Title, null, Priority.Critical, Status.Completed, null), "Edit accepts empty optional fields");
            var edited = service.GorevGetir(target.Id)!;
            Check((edited.Id, edited.CodeNumber, edited.CreatedDate) == identity && edited.Description == "" && edited.DueDate == null && edited.Status == Status.Completed, "Edit preserves Guid/code/created date and updates fields");
            var invalidRejected = false;
            try { service.GorevGuncelle(target.Id, " ", "bad", (Priority)99, Status.Todo, null); }
            catch (GecersizGorevException) { invalidRejected = true; }
            Check(invalidRejected && service.GorevGetir(target.Id)!.Title == edited.Title, "Invalid edit leaves record unchanged");
            foreach (var status in new[] { Status.Todo, Status.InProgress, Status.Completed })
            {
                service.GorevDurumunuGuncelle(target.Id, status);
                using var read = Open();
                var savedStatus = await read.Tasks.AsNoTracking().SingleAsync(t => t.Id == target.Id);
                var summary = new TaskService(read, account).OzetGetir();
                Check(savedStatus.Status == status && savedStatus.CodeNumber == persisted.CodeNumber, "Persisted status transition preserves code: " + status);
                Check(summary.Total == 12 && summary.Todo == (status == Status.Todo ? 12 : 11)
                    && summary.InProgress == (status == Status.InProgress ? 1 : 0)
                    && summary.Completed == (status == Status.Completed ? 1 : 0), "Summary reflects status transition: " + status);
            }
            Check(service.GorevGetir(Guid.NewGuid()) is null && !service.GorevGuncelle(Guid.NewGuid(), "Missing", null, Priority.Low, Status.Todo, null), "Missing task read and update are handled");

            await using var host = await TestAuthHost.Start(connection.ConnectionString);
            using var session = host.CreateSession();
            var client = session.Client;
            await host.SignIn(session, account);
            using var readyResponse = await client.GetAsync("/Tasks/Create");
            Check(readyResponse.IsSuccessStatusCode, "MVC HTTPS test server started on isolated local port with real authenticated session");
            async Task<string> Get(string path) => await client.GetStringAsync(path);
            string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
            async Task<HttpResponseMessage> Post(string path, Dictionary<string, string> values) => await client.PostAsync(path, new FormUrlEncodedContent(values));
            var form = await Get("/Tasks/Create");
            var token = Token(form);
            Check(token.Length > 0 && form.Contains("name=\"DueDate\""), "Create form renders shared fields and antiforgery token");
            Check(Regex.IsMatch(form, @"<form\b[^>]*\bnovalidate\b") && form.Contains("required")
                && form.Contains("aria-describedby=\"title-help title-error\""), "Create delegates required validation to MVC while retaining accessible field semantics");
            var invalidCreates = new[]
            {
                (Title: "", Description: "preserved description", Priority: "3", Status: "1"),
                (Title: " ", Description: "preserved description", Priority: "1", Status: "0"),
                (Title: new string('x', 121), Description: "", Priority: "1", Status: "0"),
                (Title: "Valid title", Description: new string('x', 1001), Priority: "1", Status: "0"),
                (Title: "Valid title", Description: "", Priority: "999", Status: "0"),
                (Title: "Valid title", Description: "", Priority: "1", Status: "999")
            };
            foreach (var (title, description, priority, status) in invalidCreates)
            {
                using var invalidDb = Open();
                var beforeCount = await invalidDb.Tasks.CountAsync();
                var beforeInvalid = JsonSerializer.Serialize(await invalidDb.Tasks.AsNoTracking().OrderBy(t => t.Id).ToListAsync());
                using var invalidCreate = await Post("/Tasks/Create", new()
                {
                    ["Title"] = title, ["Description"] = description, ["Priority"] = priority,
                    ["Status"] = status, ["DueDate"] = "2026-12-20", ["__RequestVerificationToken"] = token
                });
                var html = await invalidCreate.Content.ReadAsStringAsync();
                Check(invalidCreate.StatusCode == HttpStatusCode.OK && html.Contains("validation-summary-errors"), "Invalid create returns form errors");
                Check(await invalidDb.Tasks.CountAsync() == beforeCount, "Invalid create does not insert a task");
                Check(JsonSerializer.Serialize(await invalidDb.Tasks.AsNoTracking().OrderBy(t => t.Id).ToListAsync()) == beforeInvalid,
                    "Invalid create preserves every existing task field");
                var decoded = WebUtility.HtmlDecode(html);
                var field = Regex.Match(decoded, @"<span\b[^>]*id=""title-error""[^>]*>(.*?)</span>", RegexOptions.Singleline).Groups[1].Value.Trim();
                var summary = Regex.Match(decoded, @"<div\b[^>]*class=""validation-summary-errors""[^>]*>(.*?)</div>", RegexOptions.Singleline).Groups[1].Value;
                if (string.IsNullOrWhiteSpace(title))
                {
                    const string required = "Görev başlığı boş bırakılamaz.";
                    Check(field == required && Regex.Matches(summary, Regex.Escape(required)).Count == 1,
                        "Blank/whitespace title has exact Turkish field error and one summary entry under English locale");
                    Check(Regex.IsMatch(decoded, @"<input\b[^>]*aria-invalid=""true""[^>]*id=""Title""")
                        && decoded.Contains("aria-describedby=\"title-help title-error\""), "Invalid title exposes field error to assistive technology");
                    Check(Regex.Match(decoded, @"<textarea\b[^>]*id=""Description""[^>]*>(.*?)</textarea>", RegexOptions.Singleline).Groups[1].Value.Trim() == "preserved description" && decoded.Contains("value=\"2026-12-20\"")
                        && Regex.IsMatch(decoded, $@"<option[^>]*(?:selected=""selected""[^>]*value=""{priority}""|value=""{priority}""[^>]*selected=""selected"")")
                        && Regex.IsMatch(decoded, $@"<select[^>]*id=""Status""[^>]*>.*?<option[^>]*(?:selected=""selected""[^>]*value=""{status}""|value=""{status}""[^>]*selected=""selected"")", RegexOptions.Singleline),
                        "Invalid title preserves description, priority, status and due date");
                }
                if (title.Length == 121)
                    Check(field == "Başlık en fazla 120 karakter olabilir." && summary.Contains(field), "121-character title has Turkish field and summary length errors");
                if (priority == "999")
                    Check(decoded.Contains("Geçerli bir öncelik seçin."), "Invalid priority retains Turkish validation");
                if (status == "999")
                    Check(decoded.Contains("Geçerli bir durum seçin."), "Invalid status retains Turkish validation");
            }
            var formErrorCases = new[]
            {
                (Field: "Title", Value: "", Error: "Görev başlığı boş bırakılamaz."),
                (Field: "Title", Value: " ", Error: "Görev başlığı boş bırakılamaz."),
                (Field: "Priority", Value: "999", Error: "Geçerli bir öncelik seçin."),
                (Field: "Priority", Value: "abc", Error: "Geçerli bir öncelik seçin."),
                (Field: "Status", Value: "999", Error: "Geçerli bir durum seçin."),
                (Field: "Status", Value: "abc", Error: "Geçerli bir durum seçin."),
                (Field: "DueDate", Value: "abc", Error: "Geçerli bir son teslim tarihi girin."),
                (Field: "DueDate", Value: "2026-02-30", Error: "Geçerli bir son teslim tarihi girin."),
                (Field: "DueDate", Value: "1899-12-31", Error: "1900 ile 9998 yılları arasında bir tarih seçin."),
                (Field: "DueDate", Value: "9999-01-01", Error: "1900 ile 9998 yılları arasında bir tarih seçin.")
            };
            foreach (var path in new[] { "/Tasks/Create", $"/Tasks/Edit/{target.Id}" })
            {
                var errorToken = Token(await Get(path));
                foreach (var errorCase in formErrorCases)
                {
                    using var checkDb = Open();
                    var beforeError = JsonSerializer.Serialize(await checkDb.Tasks.AsNoTracking().OrderBy(t => t.Id).ToListAsync());
                    var attempted = new Dictionary<string, string>
                    {
                        ["Title"] = marker + " retained form title", ["Description"] = "Retained description <safe>",
                        ["Priority"] = "3", ["Status"] = "1", ["DueDate"] = "2026-12-20",
                        ["__RequestVerificationToken"] = errorToken
                    };
                    attempted[errorCase.Field] = errorCase.Value;
                    using var errorResponse = await Post(path, attempted);
                    var errorHtml = WebUtility.HtmlDecode(await errorResponse.Content.ReadAsStringAsync());
                    var label = $"{path.Split('/')[2]} {errorCase.Field} '{errorCase.Value}'";
                    Check(errorResponse.StatusCode == HttpStatusCode.OK, label + " returns the editable form");
                    var summaryHtml = Regex.Match(errorHtml, @"<div\b[^>]*class=""validation-summary-errors""[^>]*>(.*?)</div>", RegexOptions.Singleline).Groups[1].Value;
                    var fieldHtml = Regex.Match(errorHtml, $@"<span\b[^>]*data-valmsg-for=""{errorCase.Field}""[^>]*>(.*?)</span>", RegexOptions.Singleline).Groups[1].Value;
                    Check(fieldHtml == errorCase.Error && Regex.Matches(summaryHtml, Regex.Escape(errorCase.Error)).Count == 1,
                        label + " has the exact Turkish field error and one summary entry under English locale");
                    Check(JsonSerializer.Serialize(await checkDb.Tasks.AsNoTracking().OrderBy(t => t.Id).ToListAsync()) == beforeError,
                        label + " leaves every stored task unchanged");
                    var descriptionValue = Regex.Match(errorHtml, @"<textarea\b[^>]*id=""Description""[^>]*>(.*?)</textarea>", RegexOptions.Singleline).Groups[1].Value.Trim();
                    var titleInput = Regex.Match(errorHtml, @"<input\b[^>]*id=""Title""[^>]*>").Value;
                    var dateInput = Regex.Match(errorHtml, @"<input\b[^>]*id=""DueDate""[^>]*>").Value;
                    Check(descriptionValue == attempted["Description"]
                        && titleInput.Contains($"value=\"{attempted["Title"]}\"")
                        && dateInput.Contains($"value=\"{attempted["DueDate"]}\""),
                        label + " retains attempted title, description and date for correction");
                }
            }
            var maximumTitle = marker + new string('x', 120 - marker.Length);
            using var maximumResponse = await Post("/Tasks/Create", new()
            {
                ["Title"] = maximumTitle, ["Description"] = "", ["Priority"] = "1", ["Status"] = "0", ["__RequestVerificationToken"] = token
            });
            using (var maximumDb = Open())
                Check(maximumResponse.StatusCode == HttpStatusCode.Redirect && await maximumDb.Tasks.AnyAsync(t => t.Title == maximumTitle),
                    "120-character valid title is accepted and persisted after invalid submissions");
            var cleanForm = await Get("/Tasks/Create");
            Check(!cleanForm.Contains("validation-summary-errors") && !cleanForm.Contains("field-validation-error")
                && cleanForm.Contains("aria-invalid=\"false\""), "Fresh create form clears previous title errors after successful submission");
            var fields = new Dictionary<string, string> { ["Title"] = marker + " HTTP <script>alert(1)</script>", ["Description"] = "", ["Priority"] = "3", ["Status"] = "0", ["DueDate"] = "", ["__RequestVerificationToken"] = token };
            using var createResponse = await Post("/Tasks/Create", fields);
            Check(createResponse.StatusCode == HttpStatusCode.Redirect, "HTTP create accepts empty description and deadline");
            using var httpDb = Open();
            var httpTask = await httpDb.Tasks.AsNoTracking().SingleAsync(t => t.Title == fields["Title"]);
            var details = await Get($"/Tasks/Details/{httpTask.Id}");
            Check(details.Contains(httpTask.Code) && !details.Contains("<script>alert(1)</script>") && details.Contains("&lt;script&gt;"), "Details displays code and HTML-encodes task text");
            var editForm = await Get($"/Tasks/Edit/{httpTask.Id}");
            fields["__RequestVerificationToken"] = Token(editForm);
            fields["Title"] = " "; fields["Description"] = "preserve this text";
            using var invalid = await Post($"/Tasks/Edit/{httpTask.Id}", fields);
            var invalidHtml = await invalid.Content.ReadAsStringAsync();
            Check(invalid.StatusCode == HttpStatusCode.OK && invalidHtml.Contains("preserve this text") && invalidHtml.Contains("validation-summary-errors"), "Invalid edit preserves inputs and shows validation summary");
            Check((await httpDb.Tasks.AsNoTracking().SingleAsync(t => t.Id == httpTask.Id)).Title == httpTask.Title, "Invalid HTTP edit did not change database");
            fields["Title"] = marker + " HTTP updated"; fields["Priority"] = "0"; fields["Status"] = "1"; fields["DueDate"] = "2026-12-20";
            fields["Code"] = "WF-999999"; fields["CodeNumber"] = "999999"; fields["CreatedDate"] = "2000-01-01";
            using var update = await Post($"/Tasks/Edit/{httpTask.Id}", fields);
            Check(update.StatusCode == HttpStatusCode.Redirect && update.Headers.Location!.ToString().Contains("Details"), "Valid HTTP edit redirects to details");
            var updated = await httpDb.Tasks.AsNoTracking().SingleAsync(t => t.Id == httpTask.Id);
            Check(updated.Title == fields["Title"] && updated.Priority == Priority.High && updated.Status == Status.InProgress && updated.DueDate == TaskDates.ToUtc(new DateOnly(2026, 12, 20)), "HTTP edit updates all editable fields");
            Check(updated.CodeNumber == httpTask.CodeNumber && updated.CreatedDate == httpTask.CreatedDate, "Forged code and created date ignored");
            fields.Remove("__RequestVerificationToken");
            using var csrf = await Post($"/Tasks/Edit/{httpTask.Id}", fields);
            Check(csrf.StatusCode == HttpStatusCode.BadRequest, "Edit rejects missing CSRF token");
            using var missing = await client.GetAsync($"/Tasks/Details/{Guid.NewGuid()}");
            Check(missing.StatusCode == HttpStatusCode.NotFound && (await missing.Content.ReadAsStringAsync()).Contains("404"), "Missing details returns readable 404");
            using var missingEdit = await client.GetAsync($"/Tasks/Edit/{Guid.NewGuid()}");
            Check(missingEdit.StatusCode == HttpStatusCode.NotFound, "Missing edit returns 404");
            var list = await Get($"/Tasks?Filter.Search={marker}&Filter.Status=0&Filter.Sort=Oldest&Filter.PageSize=5");
            Check(list.Contains("rel=\"next\"") && list.Contains("Filter.Search=") && list.Contains("Filter.Sort=Oldest") && list.Contains("Filter.Status=Todo"), "Pagination links retain search, status and sorting");
            var badFilter = await Get("/Tasks?Filter.Sort=999&Filter.Page=0");
            Check(badFilter.Contains("validation-summary-errors"), "Invalid query options show validation errors");
            count += await KanbanChecks.Run(client, httpTask.Id, Open);
            if (Environment.GetEnvironmentVariable("WORKFLOW_BROWSER_CHECKS") == "1")
            {
                var browserStart = new ProcessStartInfo("powershell") { UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
                    Path.Combine(Directory.GetCurrentDirectory(), "Tests", "KanbanBrowserChecks.ps1"),
                    "-BaseUrl", client.BaseAddress!.ToString().TrimEnd('/'), "-TaskId", httpTask.Id.ToString() })
                    browserStart.ArgumentList.Add(argument);
                host.ConfigureBrowser(browserStart, session);
                using var browser = Process.Start(browserStart)!;
                var browserOutput = browser.StandardOutput.ReadToEndAsync();
                var browserErrors = browser.StandardError.ReadToEndAsync();
                await browser.WaitForExitAsync();
                var output = await browserOutput;
                Console.Write(output);
                if (browser.ExitCode != 0)
                    throw new InvalidOperationException("Kanban browser checks failed: " + await browserErrors);
                count += Regex.Matches(output, "(?m)^PASS:").Count;
            }
            fields = new() { ["gorevId"] = httpTask.Id.ToString(), ["Filter.Search"] = marker, ["Filter.Sort"] = "Oldest", ["Filter.Page"] = "2", ["__RequestVerificationToken"] = Token(list) };
            using var delete = await Post("/Tasks/Delete", fields);
            Check(delete.StatusCode == HttpStatusCode.Redirect && delete.Headers.Location!.ToString().Contains("Filter.Page=2") && !await httpDb.Tasks.AnyAsync(t => t.Id == httpTask.Id), "HTTP deletion preserves list context and removes only test task");
            count += await CalendarChecks.Run(client, Open, account, host, session);
            count += await AuthenticationChecks.Run(host, Open);
        }
        finally
        {
            using var cleanup = Open();
            await cleanup.Tasks.Where(t => t.Title.StartsWith(marker)).ExecuteDeleteAsync();
        }
        using var final = Open();
        Check(JsonSerializer.Serialize(await final.Tasks.AsNoTracking().OrderBy(t => t.Id).ToListAsync()) == before, "All original tasks and permanent codes preserved; test rows cleaned up");
        Check((await final.Database.GetAppliedMigrationsAsync()).SequenceEqual(migrations), "Migration history unchanged");
        Console.WriteLine($"{count} Stage 2 database/HTTP checks passed.");
    }
}
