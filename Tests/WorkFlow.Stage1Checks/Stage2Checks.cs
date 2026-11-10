using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
        var config = new ConfigurationBuilder().AddUserSecrets(typeof(WorkFlowDbContext).Assembly, false).Build();
        var connection = new NpgsqlConnectionStringBuilder(config.GetConnectionString("WorkFlowDb"));
        if (connection.Database != "workflow_db" || connection.Host is not ("localhost" or "127.0.0.1"))
            throw new InvalidOperationException("Only local workflow_db is allowed");
        connection.IncludeErrorDetail = false;
        WorkFlowDbContext Open() => new(new DbContextOptionsBuilder<WorkFlowDbContext>().UseNpgsql(connection.ConnectionString).Options);
        int count = 0;
        void Check(bool ok, string message)
        { if (!ok) throw new InvalidOperationException(message); Console.WriteLine("PASS: " + message); count++; }
        using var initial = Open();
        var before = JsonSerializer.Serialize(await initial.Tasks.AsNoTracking().OrderBy(t => t.Id).ToListAsync());
        var migrations = (await initial.Database.GetAppliedMigrationsAsync()).ToArray();
        Check(migrations.Length == 2, "Two existing migrations, no new schema required");
        var marker = "STAGE2-" + Guid.NewGuid().ToString("N");
        Process? server = null;
        try
        {
            using var db = Open();
            var service = new TaskService(db);
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
            var identity = (target.Id, target.CodeNumber, target.CreatedDate);
            Check(service.GorevGuncelle(target.Id, target.Title, null, Priority.Critical, Status.Completed, null), "Edit accepts empty optional fields");
            var edited = service.GorevGetir(target.Id)!;
            Check((edited.Id, edited.CodeNumber, edited.CreatedDate) == identity && edited.Description == "" && edited.DueDate == null && edited.Status == Status.Completed, "Edit preserves Guid/code/created date and updates fields");
            var invalidRejected = false;
            try { service.GorevGuncelle(target.Id, " ", "bad", (Priority)99, Status.Todo, null); }
            catch (GecersizGorevException) { invalidRejected = true; }
            Check(invalidRejected && service.GorevGetir(target.Id)!.Title == edited.Title, "Invalid edit leaves record unchanged");
            Check(service.GorevGetir(Guid.NewGuid()) is null && !service.GorevGuncelle(Guid.NewGuid(), "Missing", null, Priority.Low, Status.Todo, null), "Missing task read and update are handled");

            var portProbe = new TcpListener(IPAddress.Loopback, 0); portProbe.Start();
            var port = ((IPEndPoint)portProbe.LocalEndpoint).Port; portProbe.Stop();
            var start = new ProcessStartInfo("dotnet") { WorkingDirectory = Directory.GetCurrentDirectory(), UseShellExecute = false,
                CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(Path.Combine(Directory.GetCurrentDirectory(), "bin", "Release", "net10.0", "WorkFlow.Mvc.dll"));
            start.ArgumentList.Add("--urls"); start.ArgumentList.Add($"http://127.0.0.1:{port}");
            start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            start.Environment["DOTNET_ENVIRONMENT"] = "Development";
            start.Environment["ConnectionStrings__WorkFlowDb"] = connection.ConnectionString;
            start.Environment["Logging__LogLevel__Default"] = "Critical";
            server = Process.Start(start)!;
            server.OutputDataReceived += (_, _) => { }; server.ErrorDataReceived += (_, _) => { };
            server.BeginOutputReadLine(); server.BeginErrorReadLine();
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() })
            { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(20) };
            var ready = false;
            for (int i = 0; i < 40; i++)
            {
                try { using var response = await client.GetAsync("/Tasks/Create"); if (response.IsSuccessStatusCode) { ready = true; break; } }
                catch (HttpRequestException) { }
                await Task.Delay(250);
            }
            Check(ready, "MVC test server started on isolated local port");
            async Task<string> Get(string path) => await client.GetStringAsync(path);
            string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
            async Task<HttpResponseMessage> Post(string path, Dictionary<string, string> values) => await client.PostAsync(path, new FormUrlEncodedContent(values));
            var form = await Get("/Tasks/Create");
            var token = Token(form);
            Check(token.Length > 0 && form.Contains("name=\"DueDate\""), "Create form renders shared fields and antiforgery token");
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
            fields = new() { ["gorevId"] = httpTask.Id.ToString(), ["Filter.Search"] = marker, ["Filter.Sort"] = "Oldest", ["Filter.Page"] = "2", ["__RequestVerificationToken"] = Token(list) };
            using var delete = await Post("/Tasks/Delete", fields);
            Check(delete.StatusCode == HttpStatusCode.Redirect && delete.Headers.Location!.ToString().Contains("Filter.Page=2") && !await httpDb.Tasks.AnyAsync(t => t.Id == httpTask.Id), "HTTP deletion preserves list context and removes only test task");
        }
        finally
        {
            if (server is not null) { if (!server.HasExited) { server.Kill(true); await server.WaitForExitAsync(); } server.Dispose(); }
            using var cleanup = Open();
            await cleanup.Tasks.Where(t => t.Title.StartsWith(marker)).ExecuteDeleteAsync();
        }
        using var final = Open();
        Check(JsonSerializer.Serialize(await final.Tasks.AsNoTracking().OrderBy(t => t.Id).ToListAsync()) == before, "All original tasks and permanent codes preserved; test rows cleaned up");
        Check((await final.Database.GetAppliedMigrationsAsync()).SequenceEqual(migrations), "Migration history unchanged");
        Console.WriteLine($"{count} Stage 2 database/HTTP checks passed.");
    }
}
