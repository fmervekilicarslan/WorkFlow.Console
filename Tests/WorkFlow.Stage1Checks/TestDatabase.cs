using System.Text.RegularExpressions;
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
        await using var db = new WorkFlowDbContext(new DbContextOptionsBuilder<WorkFlowDbContext>()
            .UseNpgsql(connection.ConnectionString).Options);
        await db.Database.MigrateAsync();
        Console.WriteLine("PASS: Migrations applied only to verified test database.");
    }
}
