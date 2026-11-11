using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WorkFlow.Mvc.Data;
using WorkFlow.Mvc.Enums;
using WorkFlow.Mvc.Models;
using WorkFlow.Mvc.Services;

internal static class DatabaseChecks
{
    public static async Task Run()
    {
        var connection = await TestDatabase.VerifiedConnection();
        WorkFlowDbContext Open() => new(new DbContextOptionsBuilder<WorkFlowDbContext>().UseNpgsql(connection.ConnectionString).Options);
        var checkedCount = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            Console.WriteLine($"PASS: {message}");
            checkedCount++;
        }
        using var baselineDb = Open();
        var applied = (await baselineDb.Database.GetAppliedMigrationsAsync()).ToArray();
        Check(applied.SequenceEqual(new[] { "20261104172505_InitialPostgres", "20261109075834_AddPersistentTaskCodes" }), "Expected two migrations applied");
        var original = await baselineDb.Tasks.AsNoTracking().OrderBy(t => t.Id).ToListAsync();
        var originalJson = JsonSerializer.Serialize(original);
        var owned = new ConcurrentDictionary<Guid, byte>();
        var marker = "STAGE1-VERIFY-" + Guid.NewGuid().ToString("N");
        TaskItem NewTask(string label, Priority priority = Priority.Critical, Status status = Status.Todo)
        {
            var item = new TaskItem(marker + " " + label, marker + " description 50%_literal", priority, status)
            { DueDate = TaskDates.ToUtc(new DateOnly(2026, 11, 20)) };
            owned.TryAdd(item.Id, 0);
            return item;
        }
        try
        {
            using var db = Open();
            var service = new TaskService(db);
            var task = NewTask("CRUD");
            service.GorevEkle(task);
            Check(task.CodeNumber > 0 && task.Id != Guid.Empty, "Create assigns sequence code while retaining Guid");
            var saved = service.TumGorevleriGetir(marker, Status.Todo, Priority.Critical).Single();
            Check(saved.Id == task.Id && saved.CodeNumber == task.CodeNumber && saved.Title == task.Title, "Read with combined search/status/priority");
            Check(saved.DueDate == task.DueDate && saved.DueDate!.Value.Kind == DateTimeKind.Utc, "DueDate round trips as UTC");
            Check(service.TumGorevleriGetir("50%_literal").Any(t => t.Id == task.Id), "Literal percent/underscore search matches description");
            service.GorevDurumunuGuncelle(task.Id, Status.InProgress);
            using (var read = Open())
            {
                var updated = await read.Tasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
                Check(updated.Status == Status.InProgress && updated.CodeNumber == task.CodeNumber && updated.Id == task.Id, "Update status preserves permanent identity and code");
            }
            Check(service.TumGorevleriGetir(marker, Status.Todo, Priority.Critical).Count == 0, "Status filter excludes updated task");
            Check(service.TumGorevleriGetir(marker, Status.InProgress, Priority.Low).Count == 0, "Priority filter combines with status");
            Check(service.OzetGetir().Total == original.Count + 1, "Summary remains independent from filtered results");
            var deletedCode = task.CodeNumber;
            service.GorevSil(task.Id);
            Check(!await db.Tasks.AsNoTracking().AnyAsync(t => t.Id == task.Id), "Delete removes only the test task");
            using var barrier = new Barrier(8);
            var jobs = Enumerable.Range(0, 8).Select(i => Task.Factory.StartNew(() =>
            {
                using var concurrentDb = Open();
                var item = NewTask("Concurrent " + i);
                if (!barrier.SignalAndWait(TimeSpan.FromSeconds(30))) throw new InvalidOperationException("Concurrent start timed out");
                new TaskService(concurrentDb).GorevEkle(item);
                return item;
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
            var created = await Task.WhenAll(jobs);
            Check(created.Select(t => t.CodeNumber).Distinct().Count() == 8 && created.All(t => t.CodeNumber > deletedCode), "Eight simultaneous inserts get distinct codes and never reuse deleted code");
            using (var read = Open())
            {
                var result = new TaskService(read).TumGorevleriGetir(marker);
                Check(result.Count == 8 && result.Select(t => t.Id).ToHashSet().SetEquals(created.Select(t => t.Id)), "All concurrent inserts persisted");
                Check(result.Select(t => t.Id).SequenceEqual(result.OrderByDescending(t => t.CreatedDate).ThenBy(t => t.Id).Select(t => t.Id)), "PostgreSQL returns newest tasks first");
            }
            var duplicateId = Guid.NewGuid();
            owned.TryAdd(duplicateId, 0);
            var rejected = false;
            using (var duplicate = Open())
            {
                try
                {
                    await duplicate.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Tasks\" (\"Id\", \"Title\", \"Description\", \"Status\", \"Priority\", \"CreatedDate\", \"CodeNumber\") VALUES ({duplicateId}, {marker}, {marker}, {0}, {0}, {DateTime.UtcNow}, {created[0].CodeNumber})");
                }
                catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation && ex.ConstraintName == "IX_Tasks_CodeNumber")
                { rejected = true; }
            }
            Check(rejected, "Database rejects duplicate CodeNumber with unique index");
            var latest = created.MaxBy(t => t.CodeNumber)!;
            using (var delete = Open()) new TaskService(delete).GorevSil(latest.Id);
            using (var insert = Open())
            {
                var replacement = NewTask("After delete");
                new TaskService(insert).GorevEkle(replacement);
                Check(replacement.CodeNumber > latest.CodeNumber, "Deleting largest code does not recycle its number");
            }
        }
        finally
        {
            // Explicitly authorized test cleanup: only GUIDs allocated by this run and its marker.
            using var cleanup = Open();
            var ids = owned.Keys.ToArray();
            await cleanup.Tasks.Where(t => ids.Contains(t.Id) && t.Title.StartsWith(marker)).ExecuteDeleteAsync();
        }
        using var verify = Open();
        var remaining = await verify.Tasks.AsNoTracking().OrderBy(t => t.Id).ToListAsync();
        Check(JsonSerializer.Serialize(remaining) == originalJson, "All original fields and codes unchanged; test records fully cleaned up");
        Console.WriteLine($"{checkedCount} PostgreSQL checks passed. Final task count: {remaining.Count}.");
    }
}
