internal static class IsolationChecks
{
    public static void Run()
    {
        var name = "workflow_test_" + new string('a', 32);
        var valid = $"Host=127.0.0.1;Port=5433;Database={name};Username={name};Password=offline-placeholder";
        var rejected = new (string Label, string? Value)[]
        {
            ("missing connection", null), ("empty connection", ""),
            ("malformed connection", "not-a-connection"),
            ("live target", valid.Replace(name, "workflow_db")),
            ("case variant of live target", valid.Replace(name, "WORKFLOW_DB")),
            ("generic database", valid.Replace(name, "postgres")),
            ("non-unique test name", valid.Replace(name, "workflow_test")),
            ("remote host", valid.Replace("127.0.0.1", "example.invalid")),
            ("multiple hosts", valid.Replace("127.0.0.1", "127.0.0.1,localhost")),
            ("implicit port", valid.Replace("Port=5433;", "")),
            ("administrator role", valid.Replace($"Username={name}", "Username=postgres")),
            ("missing password", valid.Replace("Password=offline-placeholder", "Password=")),
            ("extra connection options", valid + ";Options=-c role=postgres")
        };
        foreach (var (label, value) in rejected)
        {
            var refused = false;
            try { TestDatabase.Validate(value); }
            catch (InvalidOperationException) { refused = true; }
            if (!refused) throw new InvalidOperationException("Isolation guard accepted " + label);
            Console.WriteLine("PASS: Isolation guard rejects " + label);
        }
        TestDatabase.Validate(valid);
        Console.WriteLine("PASS: Isolation guard accepts explicit local test configuration without connecting.");
        Console.WriteLine($"{rejected.Length + 1} isolation checks passed. Database was not contacted.");
    }
}
