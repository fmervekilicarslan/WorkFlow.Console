using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WorkFlow.Mvc.Data;
using WorkFlow.Mvc.Interfaces;
using WorkFlow.Mvc.Models;

internal sealed record TestCurrentUser(string? UserId) : ICurrentUser;

internal sealed class TestAccount : IAsyncDisposable, ICurrentUser
{
    private readonly Func<WorkFlowDbContext> open;
    public string UserId { get; }
    public string Email { get; }
    public string Password { get; } = "Qa!9aZ" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    private TestAccount(Func<WorkFlowDbContext> open, string id, string email) { this.open = open; UserId = id; Email = email; }
    public static async Task<TestAccount> Create(Func<WorkFlowDbContext> open)
    {
        var id = Guid.NewGuid().ToString("N");
        var account = new TestAccount(open, id, "qa-" + id + "@example.invalid");
        var user = new ApplicationUser { Id = id, Email = account.Email, UserName = account.Email,
            NormalizedEmail = account.Email.ToUpperInvariant(), NormalizedUserName = account.Email.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString(), LockoutEnabled = true };
        user.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(user, account.Password);
        using var db = open(); db.Users.Add(user); await db.SaveChangesAsync();
        return account;
    }
    public async ValueTask DisposeAsync()
    {
        using var db = open();
        if (await db.Tasks.AnyAsync(t => t.OwnerId == UserId)) throw new InvalidOperationException("Owned task fixtures must be cleaned before their test account.");
        await db.Users.Where(u => u.Id == UserId && u.Email == Email).ExecuteDeleteAsync();
    }
}

internal sealed class TestSession : IDisposable
{
    public CookieContainer Cookies { get; } = new();
    public HttpClient Client { get; }
    public TestSession(Uri baseUri, X509Certificate2 certificate)
    {
        var pin = certificate.GetCertHashString(HashAlgorithmName.SHA256);
        var handler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = Cookies,
            ServerCertificateCustomValidationCallback = (request, actual, _, _) => request.RequestUri is { Scheme: "https", Host: "127.0.0.1" }
                && request.RequestUri.Port == baseUri.Port && actual?.GetCertHashString(HashAlgorithmName.SHA256) == pin };
        Client = new HttpClient(handler) { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(20) };
        Client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
    }
    public void Dispose() => Client.Dispose();
}

internal sealed class TestAuthHost : IAsyncDisposable
{
    private readonly X509Certificate2 certificate;
    private readonly Process server;
    public Uri BaseUri { get; }
    public string CertificateSpki { get; }
    private TestAuthHost(Process server, X509Certificate2 certificate, Uri baseUri, string spki)
    { this.server = server; this.certificate = certificate; BaseUri = baseUri; CertificateSpki = spki; }
    public static async Task<TestAuthHost> Start(string connectionString)
    {
        var supplied = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
        TestDatabase.Validate(new Npgsql.NpgsqlConnectionStringBuilder { Host = supplied.Host, Port = supplied.Port,
            Database = supplied.Database, Username = supplied.Username, Password = supplied.Password }.ConnectionString);
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=WorkFlow isolated QA", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder(); san.AddIpAddress(IPAddress.Loopback); san.AddDnsName("localhost");
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(4));
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var path = Path.Combine(Directory.GetCurrentDirectory(), ".local-checkpoints", "qa-tls-" + Guid.NewGuid().ToString("N") + ".pfx");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var pfx = generated.Export(X509ContentType.Pfx, password); await File.WriteAllBytesAsync(path, pfx);
        var certificate = X509CertificateLoader.LoadPkcs12(pfx, password);
        var spki = Convert.ToBase64String(SHA256.HashData(key.ExportSubjectPublicKeyInfo()));
        var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start(); var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        var baseUri = new Uri($"https://127.0.0.1:{port}");
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = Directory.GetCurrentDirectory(), UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.Combine(Directory.GetCurrentDirectory(), "bin", "Release", "net10.0", "WorkFlow.Mvc.dll"));
        start.ArgumentList.Add("--urls"); start.ArgumentList.Add(baseUri.ToString().TrimEnd('/'));
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development"; start.Environment["DOTNET_ENVIRONMENT"] = "Development";
        start.Environment["ConnectionStrings__WorkFlowDb"] = connectionString;
        start.Environment["Kestrel__Certificates__Default__Path"] = path;
        start.Environment["Kestrel__Certificates__Default__Password"] = password;
        start.Environment["Logging__LogLevel__Default"] = "Critical";
        var server = Process.Start(start)!;
        server.OutputDataReceived += (_, _) => { }; server.ErrorDataReceived += (_, _) => { };
        server.BeginOutputReadLine(); server.BeginErrorReadLine();
        var host = new TestAuthHost(server, certificate, baseUri, spki);
        using var session = host.CreateSession();
        for (var i = 0; i < 60; i++)
        {
            try { using var response = await session.Client.GetAsync("/Account/Login"); if (response.IsSuccessStatusCode) return host; }
            catch (HttpRequestException) { }
            await Task.Delay(200);
        }
        await host.DisposeAsync(); throw new InvalidOperationException("Isolated HTTPS Identity server did not start; provider output suppressed.");
    }
    public TestSession CreateSession() => new(BaseUri, certificate);
    public static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    public async Task SignIn(TestSession session, TestAccount account)
    {
        var token = Token(await session.Client.GetStringAsync("/Account/Login"));
        using var response = await session.Client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["Email"] = account.Email, ["Password"] = account.Password, ["__RequestVerificationToken"] = token }));
        if (response.StatusCode != HttpStatusCode.Redirect || session.Cookies.GetCookies(BaseUri)[".WorkFlow.Identity"] is null)
            throw new InvalidOperationException("Real isolated account login did not issue an authenticated cookie.");
    }
    public void ConfigureBrowser(ProcessStartInfo start, TestSession session)
    {
        var cookie = session.Cookies.GetCookies(BaseUri)[".WorkFlow.Identity"];
        if (cookie is null || !cookie.Secure || !cookie.HttpOnly) throw new InvalidOperationException("A secure real-login cookie is required for authenticated browser regression checks.");
        start.Environment["WORKFLOW_BROWSER_CERT_SPKI"] = CertificateSpki;
        start.Environment["WORKFLOW_BROWSER_COOKIE"] = cookie.Value;
    }
    public async ValueTask DisposeAsync()
    {
        if (!server.HasExited) { server.Kill(true); await server.WaitForExitAsync(); }
        server.Dispose(); certificate.Dispose();
    }
}
