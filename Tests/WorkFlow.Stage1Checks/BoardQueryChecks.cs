using System.Data.Common;
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

internal static class BoardQueryChecks
{
    public static async Task<int> Run(HttpClient client, Guid taskId, Func<WorkFlowDbContext> open)
    {
        var count = 0;
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            count++; Console.WriteLine("PASS: Board V1.1 " + label);
        }
        using var setup = open();
        var target = await setup.Tasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        var marker = target.Title[..39];
        var owned = await setup.Tasks.Where(t => t.Title.StartsWith(marker)).OrderBy(t => t.Id).ToListAsync();
        Check(marker.StartsWith("STAGE2-") && owned.Count > 10, "dataset contains more than ten owned tasks");
        for (var i = 0; i < owned.Count; i++) owned[i].Status = i < 8 ? Status.Todo : i < 11 ? Status.InProgress : Status.Completed;
        owned.Single(t => t.Id == taskId).Status = Status.Completed;
        await setup.SaveChangesAsync();
        var search = Uri.EscapeDataString(marker);
        string[] Ids(string html, bool board) => Regex.Matches(html, board
            ? "data-board-card[^>]*data-task-id=\"([^\"]+)\"" : "data-list-task=\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value).Order().ToArray();
        var board = await client.GetStringAsync($"/Tasks?view=board&Filter.Search={search}");
        Check(Ids(board, true).SequenceEqual(owned.Select(t => t.Id.ToString()).Order()), "board includes every matched task beyond first page");
        Check(!board.Contains("data-list-task="), "board response excludes list task markup");
        foreach (var status in Enum.GetValues<Status>())
        {
            var section = Regex.Match(board, $"<section\\b[^>]*data-board-column=\"{(int)status}\"[^>]*>(.*?)</section>", RegexOptions.Singleline).Value;
            Check(Ids(section, true).Length == owned.Count(t => t.Status == status) && Ids(section, true).Length > 0,
                "complete column grouping: " + status);
        }
        foreach (var legacy in new[] { "Filter.Status=0&Filter.Page=2&Filter.PageSize=5&Filter.Sort=Oldest",
                     "Filter.Status=999&Filter.Page=0&Filter.PageSize=999&Filter.Sort=999",
                     "Filter.Status=abc&Filter.Page=abc&Filter.PageSize=abc&Filter.Sort=abc" })
        {
            var html = await client.GetStringAsync($"/Tasks?view=board&Filter.Search={search}&{legacy}");
            Check(Ids(html, true).SequenceEqual(Ids(board, true)) && !html.Contains("validation-summary-errors"),
                "legacy list-only parameters do not restrict or invalidate board: " + legacy);
        }
        Check(!Regex.IsMatch(board, "<select[^>]*(?:id|name)=\"Filter[._](?:Status|PageSize|Sort)\"")
            && !board.Contains("class=\"pen-pagination\"") && !board.Contains("class=\"pen-status-tabs\""),
            "board omits status tabs, pagination and list-only selectors");
        var list = await client.GetStringAsync($"/Tasks?view=list&Filter.Search={search}&Filter.PageSize=50");
        Check(Ids(list, false).SequenceEqual(Ids(board, true)) && !list.Contains("data-board-card"), "only active view renders and complete list matches board");
        foreach (var priority in Enum.GetValues<Priority>())
        {
            var html = await client.GetStringAsync($"/Tasks?view=board&Filter.Search={search}&Filter.Priority={(int)priority}");
            Check(Ids(html, true).SequenceEqual(owned.Where(t => t.Priority == priority).Select(t => t.Id.ToString()).Order()),
                "board combines search and priority: " + priority);
        }
        var byTitle = await client.GetStringAsync("/Tasks?view=board&Filter.Search=" + Uri.EscapeDataString(target.Title));
        Check(Ids(byTitle, true).SequenceEqual(new[] { taskId.ToString() }), "board search restricts matching title");
        var page = await client.GetStringAsync($"/Tasks?view=list&Filter.Search={search}&Filter.PageSize=5&Filter.Page=2&Filter.Status=0");
        Check(Ids(page, false).Length <= 5 && Ids(page, false).All(id => owned.Single(t => t.Id.ToString() == id).Status == Status.Todo),
            "list retains status filtering and pagination");
        var invalid = await client.GetStringAsync("/Tasks?view=list&Filter.Page=abc&Filter.Status=abc");
        Check(invalid.Contains("validation-summary-errors") && Ids(invalid, false).Length == 0, "list retains validation of malformed list options");
        var connectionString = setup.Database.GetConnectionString()!;
        foreach (var view in new[] { "board", "list" })
        {
            var commands = new QueryCounter();
            using var db = new WorkFlowDbContext(new DbContextOptionsBuilder<WorkFlowDbContext>()
                .UseNpgsql(connectionString).AddInterceptors(commands).Options);
            var controller = new TasksController(new TaskService(db), new Project("QA"));
            var result = (ViewResult)controller.Index(new TaskFilterViewModel { Search = marker }, view);
            var model = (TaskListViewModel)result.Model!;
            Check(commands.Sql.Count == (view == "board" ? 2 : 3), view + " executes expected SQL command count");
            Check(!db.ChangeTracker.Entries<TaskItem>().Any(), view + " reads tasks without tracking");
            Check(model.ViewMode == view && model.Tasks.Count == (view == "board" ? owned.Count : 10), view + " model contains correct active-view task set");
            if (view == "board") Check(!commands.Sql[0].Contains("LIMIT") && !commands.Sql[0].Contains("OFFSET"), "board task SQL has no pagination");
        }
        return count;
    }

    private sealed class QueryCounter : DbCommandInterceptor
    {
        public List<string> Sql { get; } = [];
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Sql.Add(command.CommandText); return result;
        }
    }
}
