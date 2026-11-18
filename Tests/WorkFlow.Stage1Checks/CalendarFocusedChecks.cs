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
        await using var account = await TestAccount.Create(Open);
        async Task<string> Snapshot() { using var db = Open(); return JsonSerializer.Serialize(await db.Tasks.AsNoTracking().OrderBy(t => t.Id).ToListAsync()); }
        var before = await Snapshot();
        var marker = "CALENDAR-" + Guid.NewGuid().ToString("N");
        var tasks = Enumerable.Range(0, 10).Select(i => new TaskItem(marker + " " + (i == 3 ? "busy-long-" + new string('ğ', 120 - marker.Length - 11) : "busy-" + i), "", Priority.Normal, Status.Todo)
            { DueDate = TaskDates.ToUtc(new DateOnly(2028, 2, 29)).AddHours(i) }).ToArray();
        foreach (var task in tasks) task.AssignOwner(account.UserId);
        try
        {
            using (var db = Open()) { db.Tasks.AddRange(tasks); await db.SaveChangesAsync(); }
            await using var host = await TestAuthHost.Start(connection.ConnectionString);
            using var session = host.CreateSession();
            await host.SignIn(session, account);
            var browserStart = new ProcessStartInfo("powershell") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(Directory.GetCurrentDirectory(), "Tests", "CalendarBrowserChecks.ps1"), "-BaseUrl", host.BaseUri.ToString().TrimEnd('/'), "-Search", marker, "-TaskId", tasks[0].Id.ToString(), "-FocusedLongTitle" }) browserStart.ArgumentList.Add(arg);
            host.ConfigureBrowser(browserStart, session);
            using var browser = Process.Start(browserStart)!;
            var output = browser.StandardOutput.ReadToEndAsync(); var error = browser.StandardError.ReadToEndAsync();
            await browser.WaitForExitAsync(); Console.Write(await output);
            if (browser.ExitCode != 0) throw new InvalidOperationException("Focused browser failed: " + await error);
        }
        finally
        {
            using var cleanup = Open(); await cleanup.Tasks.Where(t => t.Title.StartsWith(marker)).ExecuteDeleteAsync();
            if (await Snapshot() != before) throw new InvalidOperationException("Focused fixture cleanup snapshot differs.");
            Console.WriteLine("Focused owned fixtures cleaned; original test tasks unchanged.");
        }
    }
}
