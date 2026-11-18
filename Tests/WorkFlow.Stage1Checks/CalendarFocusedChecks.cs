using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WorkFlow.Mvc.Data;
using WorkFlow.Mvc.Enums;
using WorkFlow.Mvc.Models;
using WorkFlow.Mvc.Services;

internal static class CalendarFocusedChecks
{
    public static async Task Run()
    {
        var connection = await TestDatabase.VerifiedConnection();
        WorkFlowDbContext Open() => new(new DbContextOptionsBuilder<WorkFlowDbContext>().UseNpgsql(connection.ConnectionString).Options);
        async Task<string> Snapshot() { using var db = Open(); return JsonSerializer.Serialize(await db.Tasks.AsNoTracking().OrderBy(t => t.Id).ToListAsync()); }
        var before = await Snapshot();
        var marker = "CALENDAR-" + Guid.NewGuid().ToString("N");
        var tasks = Enumerable.Range(0, 10).Select(i => new TaskItem(marker + " " + (i == 3 ? "busy-long-" + new string('ğ', 120 - marker.Length - 11) : "busy-" + i), "", Priority.Normal, Status.Todo)
            { DueDate = TaskDates.ToUtc(new DateOnly(2028, 2, 29)).AddHours(i) }).ToArray();
        Process? server = null;
        try
        {
            using (var db = Open()) { db.Tasks.AddRange(tasks); await db.SaveChangesAsync(); }
            var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(Path.Combine(Directory.GetCurrentDirectory(), "bin", "Release", "net10.0", "WorkFlow.Mvc.dll"));
            start.ArgumentList.Add("--urls"); start.ArgumentList.Add($"http://127.0.0.1:{port}");
            start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            start.Environment["DOTNET_ENVIRONMENT"] = "Development";
            start.Environment["ConnectionStrings__WorkFlowDb"] = connection.ConnectionString;
            start.Environment["Logging__LogLevel__Default"] = "Critical";
            server = Process.Start(start)!;
            server.OutputDataReceived += (_, _) => { }; server.ErrorDataReceived += (_, _) => { };
            server.BeginOutputReadLine(); server.BeginErrorReadLine();
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
            var ready = false;
            for (var i = 0; i < 40; i++)
            {
                try { using var response = await client.GetAsync("/Calendar?view=month"); if (response.IsSuccessStatusCode) { ready = true; break; } }
                catch (HttpRequestException) { }
                await Task.Delay(250);
            }
            if (!ready) throw new InvalidOperationException("Focused isolated server did not start.");
            var browserStart = new ProcessStartInfo("powershell") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(Directory.GetCurrentDirectory(), "Tests", "CalendarBrowserChecks.ps1"), "-BaseUrl", client.BaseAddress.ToString().TrimEnd('/'), "-Search", marker, "-TaskId", tasks[0].Id.ToString(), "-FocusedLongTitle" }) browserStart.ArgumentList.Add(arg);
            using var browser = Process.Start(browserStart)!;
            var output = browser.StandardOutput.ReadToEndAsync(); var error = browser.StandardError.ReadToEndAsync();
            await browser.WaitForExitAsync(); Console.Write(await output);
            if (browser.ExitCode != 0) throw new InvalidOperationException("Focused browser failed: " + await error);
        }
        finally
        {
            if (server is not null) { if (!server.HasExited) { server.Kill(true); await server.WaitForExitAsync(); } server.Dispose(); }
            using var cleanup = Open(); await cleanup.Tasks.Where(t => t.Title.StartsWith(marker)).ExecuteDeleteAsync();
            if (await Snapshot() != before) throw new InvalidOperationException("Focused fixture cleanup snapshot differs.");
            Console.WriteLine("Focused owned fixtures cleaned; original test tasks unchanged.");
        }
    }
}
