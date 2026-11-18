using System.Text.RegularExpressions;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WorkFlow.Mvc.Data;

internal static class TestDatabase
{
    public const string ConnectionVariable = "WORKFLOW_TEST_CONNECTION";

    public static NpgsqlConnectionStringBuilder Validate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("An explicit WORKFLOW_TEST_CONNECTION is required; User Secrets are never used.");
        NpgsqlConnectionStringBuilder connection;
        System.Data.Common.DbConnectionStringBuilder supplied;
        try
        {
            supplied = new() { ConnectionString = value };
            connection = new(value);
        }
        catch (ArgumentException) { throw new InvalidOperationException("Invalid test connection configuration."); }
        var allowed = new[] { "Host", "Port", "Database", "Username", "Password" };
        if (supplied.Keys.Cast<string>().Any(key => !allowed.Contains(key, StringComparer.OrdinalIgnoreCase)) ||
            !supplied.ContainsKey("Port") || connection.Port is < 1 or > 65535 ||
            connection.Host is not ("localhost" or "127.0.0.1") ||
            !Regex.IsMatch(connection.Database ?? "", "^workflow_test_[a-f0-9]{32}$") ||
            connection.Username != connection.Database || string.IsNullOrWhiteSpace(connection.Password))
            throw new InvalidOperationException("Refusing unsafe or ambiguous test target. A unique local test database and matching dedicated role are required.");
        connection.IncludeErrorDetail = false;
        connection.Timeout = 10;
        return connection;
    }

    public static async Task<NpgsqlConnectionStringBuilder> VerifiedConnection()
    {
        var connection = Validate(Environment.GetEnvironmentVariable(ConnectionVariable));
        await using var db = new NpgsqlConnection(connection.ConnectionString);
        await db.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT current_database() = @database AND current_user = @role
                AND NOT (r.rolsuper OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication OR r.rolbypassrls OR r.rolinherit)
                AND d.datdba = r.oid
                AND NOT EXISTS (SELECT 1 FROM pg_auth_members WHERE member = r.oid)
                AND NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = 'workflow_db' AND datdba = r.oid)
            FROM pg_roles r JOIN pg_database d ON d.datname = current_database()
            WHERE r.rolname = current_user
            """, db);
        command.Parameters.AddWithValue("database", connection.Database!);
        command.Parameters.AddWithValue("role", connection.Username!);
        if (await command.ExecuteScalarAsync() is not true)
            throw new InvalidOperationException("Test database identity, ownership or role isolation check failed.");
        Console.WriteLine("PASS: Connected database and dedicated role verified; no administrative privileges or role memberships.");
        return connection;
    }

    public static async Task Prepare()
    {
        var connection = await VerifiedConnection();
        WorkFlowDbContext Open() => new(new DbContextOptionsBuilder<WorkFlowDbContext>().UseNpgsql(connection.ConnectionString).Options);
        var count = 0;
        void Check(bool ok, string label) { if (!ok) throw new InvalidOperationException(label); count++; Console.WriteLine("PASS: Test migration " + label); }
        await using var raw = new NpgsqlConnection(connection.ConnectionString); await raw.OpenAsync();
        async Task<string> Scalar(string sql)
        { await using var cmd = new NpgsqlCommand(sql, raw); return (await cmd.ExecuteScalarAsync())?.ToString() ?? ""; }
        const string SnapshotSql = "SELECT COALESCE(jsonb_agg(to_jsonb(t) - 'OwnerId' ORDER BY t.\"Id\"), '[]'::jsonb)::text FROM \"Tasks\" t";
        const string SequenceSql = "SELECT last_value::text || ':' || is_called::text FROM \"TaskCodeSequence\"";
        var original = await Scalar(SnapshotSql);
        var id = Guid.NewGuid(); var marker = "AUTH-MIGRATION-" + id.ToString("N");
        var inserted = false;
        try
        {
            await using (var insert = new NpgsqlCommand("INSERT INTO \"Tasks\" (\"Id\",\"Title\",\"Description\",\"Status\",\"Priority\",\"CreatedDate\",\"DueDate\") VALUES (@id,@title,@description,1,3,@created,@due)", raw))
            {
                insert.Parameters.AddWithValue("id", id); insert.Parameters.AddWithValue("title", marker);
                insert.Parameters.AddWithValue("description", "Owned migration preservation fixture");
                insert.Parameters.AddWithValue("created", new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc));
                insert.Parameters.AddWithValue("due", new DateTime(2028, 2, 28, 21, 0, 0, DateTimeKind.Utc));
                await insert.ExecuteNonQueryAsync(); inserted = true;
            }
            var legacySnapshot = await Scalar(SnapshotSql); var sequence = await Scalar(SequenceSql);
            using (var db = Open()) await db.Database.MigrateAsync();
            Check(await Scalar(SnapshotSql) == legacySnapshot, "every pre-existing task field, Guid, permanent code and timestamp survives Identity migration");
            Check(await Scalar(SequenceSql) == sequence, "Identity migration leaves task sequence unchanged");
            string legacyCode;
            using (var db = Open())
            {
                var legacy = await db.Tasks.AsNoTracking().SingleAsync(t => t.Id == id); legacyCode = legacy.Code;
                Check(legacy.OwnerId is null, "legacy fixture remains unowned rather than assigned to an account");
            }
            await using var host = await TestAuthHost.Start(connection.ConnectionString);
            await using var a = await TestAccount.Create(Open); await using var b = await TestAccount.Create(Open);
            foreach (var account in new[] { a, b })
            {
                using var session = host.CreateSession(); await host.SignIn(session, account);
                foreach (var path in new[] { "/Tasks?view=list&Filter.Search=", "/Tasks?view=board&Filter.Search=", "/Calendar?view=day&date=2028-02-29&Filter.Search=" })
                {
                    var html = await session.Client.GetStringAsync(path + marker);
                    Check(!html.Contains(id.ToString()) && !html.Contains(legacyCode), "legacy task is hidden from a real user's list, board or calendar");
                }
                using var details = await session.Client.GetAsync("/Tasks/Details/" + id);
                Check(details.StatusCode == HttpStatusCode.NotFound, "legacy task direct details access is denied for a real user");
            }
        }
        finally
        {
            if (inserted)
            {
                await using var delete = new NpgsqlCommand("DELETE FROM \"Tasks\" WHERE \"Id\"=@id AND \"Title\"=@title", raw);
                delete.Parameters.AddWithValue("id", id); delete.Parameters.AddWithValue("title", marker); await delete.ExecuteNonQueryAsync();
            }
            Check(await Scalar(SnapshotSql) == original, "owned migration fixture cleaned; all original task fields preserved");
        }
        Console.WriteLine($"{count} isolated migration preservation checks passed. No live migration was attempted.");
    }
}
