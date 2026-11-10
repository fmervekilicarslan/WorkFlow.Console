using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WorkFlow.Mvc.Data;
using WorkFlow.Mvc.Enums;
using WorkFlow.Mvc.Models;
using WorkFlow.Mvc.Services;
using WorkFlow.Mvc.ViewModels;

if (args.Contains("--stage2"))
{
    await Stage2Checks.Run();
    return;
}
if (args.Contains("--database"))
{
    await DatabaseChecks.Run();
    return;
}

var checks = 0;
void Check(bool ok, string message)
{
    if (!ok) throw new InvalidOperationException(message);
    checks++;
    Console.WriteLine($"PASS: {message}");
}
Check((int)Priority.High == 0 && (int)Priority.Normal == 1 && (int)Priority.Low == 2 && (int)Priority.Critical == 3, "Stored priority numbers stay compatible");
var today = new DateOnly(2026, 11, 9);
var utc = TaskDates.ToUtc(today);
Check(utc == new DateTime(2026, 11, 8, 21, 0, 0, DateTimeKind.Utc) && utc.Kind == DateTimeKind.Utc, "Istanbul date converts to UTC");
Check(TaskDates.LocalDate(utc) == today, "Date round trip");
Check(TaskDates.IsOverdue(TaskDates.ToUtc(today.AddDays(-1)), Status.Todo, today), "Yesterday is overdue");
Check(!TaskDates.IsOverdue(utc, Status.InProgress, today), "Due today remains valid for the whole local day");
Check(!TaskDates.IsOverdue(TaskDates.ToUtc(today.AddDays(-1)), Status.Completed, today), "Completed task is never overdue");
Check(TaskDates.IsUpcoming(utc, Status.Todo, today) && TaskDates.IsUpcoming(TaskDates.ToUtc(today.AddDays(7)), Status.Todo, today), "Upcoming includes today and day seven");
Check(!TaskDates.IsUpcoming(TaskDates.ToUtc(today.AddDays(8)), Status.Todo, today) && !TaskDates.IsUpcoming(utc, Status.Completed, today), "Upcoming excludes day eight and completed tasks");
Check(!TaskDates.IsOverdue(null, Status.Todo, today) && !TaskDates.IsUpcoming(null, Status.Todo, today), "Missing date has no deadline badge");
Check(TaskDates.LocalDate(new DateTime(2026, 11, 8, 21, 0, 0, DateTimeKind.Utc)) == today, "Local day changes at Istanbul midnight");
bool Valid(object model) => Validator.TryValidateObject(model, new ValidationContext(model), new List<ValidationResult>(), true);
Check(Valid(new TaskCreateViewModel { Title = "Valid", DueDate = today, Priority = Priority.Critical }), "Critical and selected date pass form validation");
Check(Valid(new TaskCreateViewModel { Title = "Valid" }), "DueDate is optional");
Check(!Valid(new TaskCreateViewModel { Title = " ", Priority = (Priority)999 }), "Blank title and invalid priority rejected");
Check(!Valid(new TaskCreateViewModel { Title = "Valid", DueDate = new DateOnly(1, 1, 1) }), "Unsupported date rejected before timezone conversion");
Check(!Valid(new TaskFilterViewModel { Status = (Status)999 }), "Invalid filter rejected");
using var db = new WorkFlowDbContext(new DbContextOptionsBuilder<WorkFlowDbContext>()
    .UseNpgsql("Host=localhost;Database=not_connected").Options);
var service = new TaskService(db);
var sql = service.ListeSorgusu("50%_sale", Status.InProgress, Priority.Critical).ToQueryString();
Check(sql.Contains("ILIKE") && sql.Contains(" OR ") && sql.Contains("Status") && sql.Contains("Priority") && sql.Contains("AND"), "Search and both filters translate together to PostgreSQL");
Check(sql.Contains("ORDER BY") && sql.Contains("CreatedDate\" DESC"), "Newest-first SQL ordering");
Check(sql.Contains("\\%") && sql.Contains("\\_"), "Search wildcard characters escaped");
var entity = db.Model.FindEntityType(typeof(TaskItem))!;
Check(entity.GetIndexes().Any(i => i.IsUnique && i.Properties.Any(p => p.Name == "CodeNumber")), "Task code has a database unique index");
Check(entity.FindProperty("CodeNumber")!.GetDefaultValueSql()!.Contains("nextval"), "Code is database sequence generated");
var migrationSql = db.GetService<IMigrator>().GenerateScript("20261104172505_InitialPostgres");
Check(migrationSql.Contains("CREATE SEQUENCE") && migrationSql.Contains("UPDATE \"Tasks\"") && migrationSql.Contains("CREATE UNIQUE INDEX"), "Migration backfills existing tasks and adds uniqueness");
Check(!migrationSql.Contains("DROP TABLE") && !migrationSql.Contains("DELETE FROM"), "Forward migration does not remove existing tasks");
var task = new TaskItem("Title", "Description", Priority.Normal, Status.Todo);
entity.FindProperty("CodeNumber")!.PropertyInfo!.SetValue(task, 1001L);
Check(task.Code == "WF-1001" && task.Id != Guid.Empty, "Code grows beyond three digits while retaining Guid identity");
Console.WriteLine($"{checks} checks passed. Database was not contacted.");
