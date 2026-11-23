using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WorkFlow.Mvc.Data;
using WorkFlow.Mvc.Enums;
using WorkFlow.Mvc.Models;
using WorkFlow.Mvc.Services;

internal static class AuthenticationChecks
{
    public static async Task<int> Run(TestAuthHost host, Func<WorkFlowDbContext> open)
    {
        var count = 0;
        void Check(bool ok, string label) { if (!ok) throw new InvalidOperationException(label); count++; Console.WriteLine("PASS: Identity " + label); }
        bool LoginChallenge(HttpResponseMessage response)
        {
            var location = response.Headers.Location;
            var parsed = location is not null && Uri.TryCreate(host.BaseUri, location, out _);
            var target = parsed ? new Uri(host.BaseUri, location!) : null;
            Console.WriteLine($"DIAGNOSTIC: Identity challenge status={(int)response.StatusCode}; target={(target is null ? "missing" : target.GetLeftPart(UriPartial.Authority) + target.AbsolutePath)}");
            return response.StatusCode == HttpStatusCode.Redirect && target is not null
                && target.Scheme == host.BaseUri.Scheme && target.Host == host.BaseUri.Host && target.Port == host.BaseUri.Port
                && target.AbsolutePath == "/Account/Login";
        }
        async Task<string> Snapshot() { using var db = open(); return JsonSerializer.Serialize(await db.Tasks.AsNoTracking().OrderBy(t => t.Id).ToListAsync()); }
        var original = await Snapshot();
        using var beforeDb = open();
        var originalUsers = JsonSerializer.Serialize(await beforeDb.Users.AsNoTracking().OrderBy(u => u.Id).ToListAsync());
        var marker = "AUTH-" + Guid.NewGuid().ToString("N");
        var emailA = marker.ToLowerInvariant() + "-a@example.invalid"; var emailB = marker.ToLowerInvariant() + "-b@example.invalid";
        var secret = "Qa!9zA" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var loginFailure = "Giriş yapılamadı. Bilgilerinizi kontrol edin veya bir süre sonra tekrar deneyin.";
        using var anonymous = host.CreateSession(); using var a = host.CreateSession(); using var b = host.CreateSession();
        async Task<string> Token(TestSession session, string path = "/Account/Login") => TestAuthHost.Token(await session.Client.GetStringAsync(path));
        async Task<HttpResponseMessage> Post(TestSession session, string path, Dictionary<string, string> fields, bool csrf = true, string tokenPath = "/Account/Login")
        {
            if (csrf) fields["__RequestVerificationToken"] = await Token(session, tokenPath);
            return await session.Client.PostAsync(path, new FormUrlEncodedContent(fields));
        }
        Dictionary<string, string> Credentials(string email, string password) => new() { ["Email"] = email, ["Password"] = password };
        try
        {
            foreach (var path in new[] { "/", "/Tasks?view=list", "/Tasks?view=board", "/Tasks/Create", "/Tasks/Details/" + Guid.NewGuid(), "/Tasks/Edit/" + Guid.NewGuid(), "/Calendar?view=month", "/Calendar?view=week", "/Calendar?view=day", "/Account/Profile", "/Account/Settings" })
            {
                using var response = await anonymous.Client.GetAsync(path);
                Check(LoginChallenge(response), "anonymous protected GET requires same-origin login: " + path.Split('?')[0]);
            }
            foreach (var action in new[] { "/Tasks/Create", "/Tasks/Edit/" + Guid.NewGuid(), "/Tasks/Delete", "/Tasks/UpdateStatus", "/Tasks/UpdateBoardStatus", "/Account/Logout", "/Account/Profile", "/Account/ChangePassword" })
            {
                using var response = await Post(anonymous, action, new() { ["gorevId"] = Guid.NewGuid().ToString(), ["yeniDurum"] = "1" });
                Check(LoginChallenge(response), "anonymous protected POST requires same-origin login: " + action.Split('/')[2]);
            }
            Check(await Snapshot() == original, "all anonymous requests leave every task unchanged");
            foreach (var action in new[] { "Login", "Register" })
            {
                using var page = await anonymous.Client.GetAsync("/Account/" + action);
                var html = await page.Content.ReadAsStringAsync();
                Check(page.IsSuccessStatusCode && TestAuthHost.Token(html).Length > 0 && Regex.IsMatch(html, "<form\\b[^>]*novalidate")
                    && html.Contains("autocomplete=\"" + (action == "Login" ? "current-password" : "new-password") + "\""), action + " has antiforgery, server validation and correct password autocomplete");
                Check(page.Headers.CacheControl?.NoStore == true, action + " page is not stored by shared or browser caches");
                using var csrfResponse = await Post(anonymous, "/Account/" + action, Credentials(emailA, secret), csrf: false);
                Check(csrfResponse.StatusCode == HttpStatusCode.BadRequest, action + " rejects missing antiforgery token");
            }
            foreach (var invalid in new[] { (Email: "invalid-email", Password: secret, Confirm: secret),
                         (Email: emailA, Password: "short", Confirm: "short"), (Email: emailA, Password: secret, Confirm: "mismatch"),
                         (Email: emailA, Password: "onlylowercaseletters", Confirm: "onlylowercaseletters") })
            {
                using var response = await Post(anonymous, "/Account/Register", new() { ["Email"] = invalid.Email, ["Password"] = invalid.Password, ["ConfirmPassword"] = invalid.Confirm });
                var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
                Check(response.StatusCode == HttpStatusCode.OK && html.Contains("validation-summary-errors")
                    && !Regex.IsMatch(html, "type=\"password\"[^>]*value=\"[^\"]+\""), "invalid registration has Turkish feedback and never reprints password");
            }
            using (var db = open()) Check(!await db.Users.AnyAsync(u => u.Email == emailA), "invalid registrations do not create an account");
            using (var registered = await Post(a, "/Account/Register", new() { ["Email"] = emailA, ["Password"] = secret, ["ConfirmPassword"] = secret, ["ReturnUrl"] = "/Calendar?view=month" }))
            {
                Check(registered.StatusCode == HttpStatusCode.Redirect && registered.Headers.Location?.ToString() == "/Calendar?view=month", "registration signs in and honors a safe local return URL");
                var cookie = a.Cookies.GetCookies(host.BaseUri)[".WorkFlow.Identity"];
                var header = string.Join(";", registered.Headers.GetValues("Set-Cookie"));
                Check(cookie is { Secure: true, HttpOnly: true } && header.Contains("samesite=lax", StringComparison.OrdinalIgnoreCase)
                    && header.Contains("secure", StringComparison.OrdinalIgnoreCase) && cookie.Expires == DateTime.MinValue, "registration issues a session-only Secure HttpOnly SameSite=Lax cookie");
            }
            string ownerA; string ownerB;
            using (var db = open())
            {
                var user = await db.Users.AsNoTracking().SingleAsync(u => u.Email == emailA); ownerA = user.Id;
                var bytes = Convert.FromBase64String(user.PasswordHash!);
                Check(user.PasswordHash != secret && bytes.Length > 13 && bytes[0] == 1
                    && BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(5, 4)) >= 210_000
                    && new PasswordHasher<ApplicationUser>().VerifyHashedPassword(user, user.PasswordHash!, secret) != PasswordVerificationResult.Failed,
                    "real registration stores Identity V3 PBKDF2 hash with at least 210000 iterations");
            }
            using (var duplicate = await Post(anonymous, "/Account/Register", new() { ["Email"] = emailA.ToUpperInvariant(), ["Password"] = secret, ["ConfirmPassword"] = secret }))
                Check(duplicate.StatusCode == HttpStatusCode.OK && WebUtility.HtmlDecode(await duplicate.Content.ReadAsStringAsync()).Contains("Kayıt tamamlanamadı. Bilgilerinizi kontrol edin veya giriş yapmayı deneyin."), "case-insensitive duplicate registration uses generic Turkish failure");
            using (var db = open()) Check(await db.Users.CountAsync(u => u.NormalizedEmail == emailA.ToUpperInvariant()) == 1, "duplicate registration does not add a second account");
            using (var registered = await Post(b, "/Account/Register", new() { ["Email"] = emailB, ["Password"] = secret, ["ConfirmPassword"] = secret, ["ReturnUrl"] = "https://untrusted.example/" }))
                Check(registered.StatusCode == HttpStatusCode.Redirect && registered.Headers.Location is { IsAbsoluteUri: false } && !registered.Headers.Location.ToString().Contains("untrusted"), "registration refuses an external return URL");
            using (var db = open()) ownerB = (await db.Users.AsNoTracking().SingleAsync(u => u.Email == emailB)).Id;
            foreach (var (session, ownEmail, foreignEmail) in new[] { (a, emailA, emailB), (b, emailB, emailA) })
            {
                using var profile = await session.Client.GetAsync("/Account/Profile");
                using var settings = await session.Client.GetAsync("/Account/Settings");
                var profileHtml = await profile.Content.ReadAsStringAsync(); var settingsHtml = await settings.Content.ReadAsStringAsync();
                Check(profile.IsSuccessStatusCode && profile.Headers.CacheControl?.NoStore == true && profileHtml.Contains(ownEmail) && !profileHtml.Contains(foreignEmail), "profile shows only the signed-in user's email and is not cached");
                Check(settings.IsSuccessStatusCode && settings.Headers.CacheControl?.NoStore == true &&
                    settingsHtml.Contains("href=\"/Account/Profile\"") && settingsHtml.Contains("data-theme-choice=\"dark\"") &&
                    settingsHtml.Contains("id=\"settings-password-form\"") && settingsHtml.Contains("autocomplete=\"current-password\""),
                    "account settings exposes profile, theme and current-password protected change form");
            }
            using (var missingProfileCsrf = await Post(a, "/Account/Profile", new() { ["DisplayName"] = "csrf test" }, csrf: false))
                Check(missingProfileCsrf.StatusCode == HttpStatusCode.BadRequest, "profile update rejects a missing antiforgery token");
            using (var updateProfile = await Post(a, "/Account/Profile", new() { ["DisplayName"] = "  QA Görünen Ad  ", ["Email"] = emailB }, tokenPath: "/Account/Profile"))
                Check(updateProfile.StatusCode == HttpStatusCode.Redirect && updateProfile.Headers.Location?.ToString() == "/Account/Profile", "profile update accepts the display name but ignores a forged email");
            using (var refreshedProfile = await a.Client.GetAsync("/Account/Profile"))
            {
                var html = WebUtility.HtmlDecode(await refreshedProfile.Content.ReadAsStringAsync());
                Check(html.Contains("value=\"QA Görünen Ad\"") && html.Contains(emailA) && !html.Contains(emailB), "profile shows the trimmed display name and the original read-only email");
                Check(html.Contains("Görünen adınız güncellendi."), "profile update shows Turkish success feedback");
            }
            using (var db = open())
            {
                var savedA = await db.Users.AsNoTracking().SingleAsync(u => u.Id == ownerA);
                var savedB = await db.Users.AsNoTracking().SingleAsync(u => u.Id == ownerB);
                Check(savedA.DisplayName == "QA Görünen Ad" && savedA.Email == emailA && savedB.DisplayName is null, "profile update changes only the current user's display name");
            }
            using (var tooLongProfile = await Post(a, "/Account/Profile", new() { ["DisplayName"] = new string('x', 81) }, tokenPath: "/Account/Profile"))
            {
                var html = WebUtility.HtmlDecode(await tooLongProfile.Content.ReadAsStringAsync());
                Check(tooLongProfile.StatusCode == HttpStatusCode.OK && html.Contains("Görünen ad en fazla 80 karakter olabilir."), "overlong display name gets a Turkish validation error");
            }
            using (var db = open()) Check((await db.Users.AsNoTracking().SingleAsync(u => u.Id == ownerA)).DisplayName == "QA Görünen Ad", "invalid profile update preserves the saved display name");
            foreach (var badReturn in new[] { "https://untrusted.example/", "//untrusted.example/", "/\\untrusted.example/", "\\untrusted.example/" })
            {
                using var session = host.CreateSession(); var fields = Credentials(emailA, secret); fields["ReturnUrl"] = badReturn;
                using var response = await Post(session, "/Account/Login", fields);
                Check(response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location is { IsAbsoluteUri: false }
                    && !response.Headers.Location.ToString().Contains("untrusted"), "login refuses unsafe return URL syntax");
            }
            using (var session = host.CreateSession())
            {
                var fields = Credentials(emailA, secret); fields["ReturnUrl"] = "/Tasks?view=board"; fields["RememberMe"] = "true";
                using var response = await Post(session, "/Account/Login", fields);
                var cookie = session.Cookies.GetCookies(host.BaseUri)[".WorkFlow.Identity"];
                Check(response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location?.ToString() == "/Tasks?view=board", "login accepts safe local return URL");
                Check(cookie is not null && cookie.Expires.ToUniversalTime() > DateTime.UtcNow.AddMinutes(28)
                    && cookie.Expires.ToUniversalTime() <= DateTime.UtcNow.AddMinutes(31), "remembered login remains bounded by the configured thirty-minute cookie lifetime");
            }
            using (var missing = await Post(anonymous, "/Account/Login", Credentials("missing-" + emailA, secret)))
            using (var wrong = await Post(anonymous, "/Account/Login", Credentials(emailA, "Incorrect!9Password")))
            {
                var missingHtml = WebUtility.HtmlDecode(await missing.Content.ReadAsStringAsync()); var wrongHtml = WebUtility.HtmlDecode(await wrong.Content.ReadAsStringAsync());
                Check(missing.StatusCode == HttpStatusCode.OK && wrong.StatusCode == HttpStatusCode.OK && missingHtml.Contains(loginFailure) && wrongHtml.Contains(loginFailure), "unknown user and wrong password expose the same generic login failure");
                Check(!Regex.IsMatch(wrongHtml, "type=\"password\"[^>]*value=\"[^\"]+\""), "failed login never reprints its password");
            }
            await using (var locked = await TestAccount.Create(open))
            {
                using var session = host.CreateSession();
                for (var i = 0; i < 5; i++)
                {
                    using var failure = await Post(session, "/Account/Login", Credentials(locked.Email, "Incorrect!9Password"));
                    Check(failure.StatusCode == HttpStatusCode.OK && WebUtility.HtmlDecode(await failure.Content.ReadAsStringAsync()).Contains(loginFailure), "failed password attempt retains generic failure before lockout");
                }
                using (var db = open()) Check((await db.Users.AsNoTracking().SingleAsync(u => u.Id == locked.UserId)).LockoutEnd > DateTimeOffset.UtcNow.AddMinutes(14), "five failed attempts set a fifteen-minute account lockout");
                using var correctWhileLocked = await Post(session, "/Account/Login", Credentials(locked.Email, locked.Password));
                Check(correctWhileLocked.StatusCode == HttpStatusCode.OK && session.Cookies.GetCookies(host.BaseUri)[".WorkFlow.Identity"] is null, "correct credentials cannot bypass an active lockout");
            }
            var taskA = new TaskItem(marker + " own-a", "A private task", Priority.Critical, Status.Todo) { DueDate = TaskDates.ToUtc(new DateOnly(2028, 2, 29)) }; taskA.AssignOwner(ownerA);
            var taskB = new TaskItem(marker + " own-b", "B private task", Priority.Critical, Status.Todo) { DueDate = taskA.DueDate }; taskB.AssignOwner(ownerB);
            var legacy = new TaskItem(marker + " legacy", "No owner must remain invisible", Priority.Critical, Status.Todo);
            using (var db = open()) { db.Tasks.AddRange(taskA, taskB, legacy); await db.SaveChangesAsync(); }
            using (var db = open())
            {
                var anonymousService = new TaskService(db);
                Check(anonymousService.TumGorevleriGetir().Count == 0 && anonymousService.OzetGetir().Total == 0
                    && anonymousService.GorevGetir(legacy.Id) is null && anonymousService.TakvimGorevleriniGetir(taskA.DueDate!.Value, taskA.DueDate.Value.AddDays(1)).Count == 0,
                    "service with missing user context fails closed including unowned tasks");
                var refused = false; try { anonymousService.GorevEkle(new TaskItem(marker + " anonymous", "", Priority.Normal, Status.Todo)); } catch (UnauthorizedAccessException) { refused = true; }
                Check(refused, "anonymous service cannot create tasks");
                foreach (var owner in new[] { ownerA, ownerB }) Check(new TaskService(db, new TestCurrentUser(owner)).OzetGetir().Total == 1, "summary counts only the current user's tasks");
            }
            foreach (var (session, own, foreign) in new[] { (a, taskA, taskB), (b, taskB, taskA) })
            {
                foreach (var view in new[] { "list", "board" })
                {
                    foreach (var (search, visible) in new[] { (own.Code.ToLowerInvariant(), true), (foreign.Code, false), (legacy.Code, false) })
                    {
                        var byCode = await session.Client.GetStringAsync("/Tasks?view=" + view + "&Filter.Search=" + Uri.EscapeDataString(search));
                        Check(byCode.Contains(own.Id.ToString()) == visible && !byCode.Contains(foreign.Id.ToString()) &&
                            !byCode.Contains(legacy.Id.ToString()), view + " task-code search respects ownership and case");
                    }
                }
                foreach (var view in new[] { "month", "week", "day" })
                {
                    var byCode = await session.Client.GetStringAsync("/Calendar?view=" + view + "&date=2028-02-29&Filter.Search=" + Uri.EscapeDataString(own.Code.ToLowerInvariant()));
                    var foreignCode = await session.Client.GetStringAsync("/Calendar?view=" + view + "&date=2028-02-29&Filter.Search=" + Uri.EscapeDataString(foreign.Code));
                    Check(byCode.Contains(own.Id.ToString()) && !byCode.Contains(foreign.Id.ToString()) &&
                        !foreignCode.Contains(own.Id.ToString()) && !foreignCode.Contains(foreign.Id.ToString()),
                        "calendar " + view + " task-code search respects ownership");
                }
                foreach (var view in new[] { "list", "board" })
                {
                    var html = await session.Client.GetStringAsync("/Tasks?view=" + view + "&Filter.Search=" + marker + "&OwnerId=" + foreign.OwnerId);
                    Check(html.Contains(own.Id.ToString()) && !html.Contains(foreign.Id.ToString()) && !html.Contains(legacy.Id.ToString()), view + " includes own tasks and hides foreign/unowned tasks despite forged owner query");
                }
                foreach (var view in new[] { "month", "week", "day" })
                {
                    var html = await session.Client.GetStringAsync("/Calendar?view=" + view + "&date=2028-02-29&Filter.Search=" + marker);
                    Check(html.Contains(own.Id.ToString()) && !html.Contains(foreign.Id.ToString()) && !html.Contains(legacy.Id.ToString()), "calendar " + view + " includes only current user's task");
                }
                foreach (var hidden in new[] { foreign, legacy })
                {
                    foreach (var action in new[] { "Details", "Edit" })
                    {
                        using var response = await session.Client.GetAsync("/Tasks/" + action + "/" + hidden.Id);
                        Check(response.StatusCode == HttpStatusCode.NotFound, action + " denies foreign or unowned task identifiers");
                    }
                    foreach (var action in new[] { "Edit/" + hidden.Id, "Delete", "UpdateStatus", "UpdateBoardStatus" })
                    {
                        var saved = await Snapshot();
                        using var response = await Post(session, "/Tasks/" + action, new() { ["gorevId"] = hidden.Id.ToString(), ["Title"] = marker + " attack", ["Description"] = "tamper", ["Status"] = "2", ["Priority"] = "1", ["yeniDurum"] = "2", ["OwnerId"] = own.OwnerId! }, tokenPath: "/Tasks/Create");
                        Check(response.StatusCode == HttpStatusCode.NotFound && await Snapshot() == saved, action.Split('/')[0] + " denies foreign/unowned task and preserves every stored field");
                    }
                }
            }
            using (var forgedCreate = await Post(a, "/Tasks/Create", new() { ["Title"] = marker + " forged-owner", ["Description"] = "", ["Priority"] = "1", ["Status"] = "0", ["OwnerId"] = ownerB, ["CodeNumber"] = "999999999" }, tokenPath: "/Tasks/Create"))
            using (var db = open())
            {
                var created = await db.Tasks.AsNoTracking().SingleAsync(t => t.Title == marker + " forged-owner");
                Check(forgedCreate.StatusCode == HttpStatusCode.Redirect && created.OwnerId == ownerA && created.CodeNumber != 999999999, "create derives owner and permanent code on server despite forged fields");
            }
            using (var forgedEdit = await Post(a, "/Tasks/Edit/" + taskA.Id, new() { ["Title"] = taskA.Title, ["Description"] = "still A", ["Priority"] = "3", ["Status"] = "0", ["OwnerId"] = ownerB }, tokenPath: "/Tasks/Create"))
            using (var db = open()) Check(forgedEdit.StatusCode == HttpStatusCode.Redirect && (await db.Tasks.AsNoTracking().SingleAsync(t => t.Id == taskA.Id)).OwnerId == ownerA, "editing own task cannot transfer ownership through a forged field");
            var tokenA = await Token(a, "/Tasks/Create");
            var beforeForgedToken = await Snapshot();
            using (var crossUserCsrf = await b.Client.PostAsync("/Tasks/UpdateBoardStatus", new FormUrlEncodedContent(new Dictionary<string, string> { ["gorevId"] = taskB.Id.ToString(), ["yeniDurum"] = "1", ["__RequestVerificationToken"] = tokenA })))
                Check(crossUserCsrf.StatusCode == HttpStatusCode.BadRequest && await Snapshot() == beforeForgedToken, "antiforgery token from another user cannot mutate a task");
            foreach (var action in new[] { "Create", "Edit/" + taskA.Id, "Delete", "UpdateStatus", "UpdateBoardStatus" })
            {
                var beforeCsrf = await Snapshot();
                using var response = await Post(a, "/Tasks/" + action, new() { ["gorevId"] = taskA.Id.ToString(), ["Title"] = marker + " csrf", ["Priority"] = "1", ["Status"] = "0", ["yeniDurum"] = "1" }, csrf: false);
                Check(response.StatusCode == HttpStatusCode.BadRequest && await Snapshot() == beforeCsrf, "authenticated task write rejects missing CSRF without mutating data: " + action.Split('/')[0]);
            }
            using (var session = host.CreateSession())
            {
                session.Cookies.Add(host.BaseUri, new Cookie(".WorkFlow.Identity", "tampered-not-a-signed-ticket", "/") { Secure = true, HttpOnly = true });
                using var response = await session.Client.GetAsync("/Tasks");
                Check(LoginChallenge(response), "tampered authentication ticket cannot access protected tasks");
            }
            var newSecret = "Qa!8aZ" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            Dictionary<string, string> PasswordFields(string current, string next, string confirm) =>
                new() { ["CurrentPassword"] = current, ["NewPassword"] = next, ["ConfirmPassword"] = confirm };
            string hashBefore; string stampBefore; string foreignHash;
            using (var db = open())
            {
                var current = await db.Users.AsNoTracking().SingleAsync(u => u.Id == ownerA);
                hashBefore = current.PasswordHash!; stampBefore = current.SecurityStamp!;
                foreignHash = (await db.Users.AsNoTracking().SingleAsync(u => u.Id == ownerB)).PasswordHash!;
            }
            using (var response = await a.Client.GetAsync("/Account/ChangePassword"))
                Check(response.StatusCode == HttpStatusCode.MethodNotAllowed, "password changes require POST");
            using (var response = await Post(a, "/Account/ChangePassword", PasswordFields(secret, newSecret, newSecret), csrf: false))
                Check(response.StatusCode == HttpStatusCode.BadRequest, "password change rejects missing CSRF");
            var foreignToken = await Token(b, "/Account/Settings");
            var foreignFields = PasswordFields(secret, newSecret, newSecret);
            foreignFields["__RequestVerificationToken"] = foreignToken;
            using (var response = await a.Client.PostAsync("/Account/ChangePassword", new FormUrlEncodedContent(foreignFields)))
                Check(response.StatusCode == HttpStatusCode.BadRequest, "password change rejects another user's CSRF token");
            foreach (var invalid in new[]
            {
                (Current: "wrong-current-password", Next: newSecret, Confirm: newSecret, Error: "Mevcut parola doğru değil."),
                (Current: secret, Next: newSecret, Confirm: "mismatch", Error: "Yeni parolalar eşleşmiyor."),
                (Current: secret, Next: "short", Confirm: "short", Error: "Parola en az 12 karakter olmalıdır."),
                (Current: secret, Next: "onlylowercaseletters", Confirm: "onlylowercaseletters", Error: "Parola en az bir rakam içermelidir.")
            })
            {
                using var response = await Post(a, "/Account/ChangePassword", PasswordFields(invalid.Current, invalid.Next, invalid.Confirm), tokenPath: "/Account/Settings");
                var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
                Check(response.StatusCode == HttpStatusCode.OK && html.Contains(invalid.Error), "invalid password change shows Turkish feedback");
                Check(!Regex.IsMatch(html, "type=\"password\"[^>]*value=\"[^\"]+\"") &&
                    !html.Contains(invalid.Current) && !html.Contains(invalid.Next), "invalid password change never echoes supplied passwords");
            }
            using (var db = open())
            {
                var current = await db.Users.AsNoTracking().SingleAsync(u => u.Id == ownerA);
                Check(current.PasswordHash == hashBefore && current.SecurityStamp == stampBefore, "rejected password changes preserve hash and security stamp");
            }
            var changeFields = PasswordFields(secret, newSecret, newSecret);
            changeFields["UserId"] = ownerB; changeFields["Email"] = emailB;
            using (var response = await Post(a, "/Account/ChangePassword", changeFields, tokenPath: "/Account/Settings"))
            {
                Check(response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location?.ToString() == "/Account/Settings", "password change redirects to settings");
                var cookieHeader = string.Join(";", response.Headers.GetValues("Set-Cookie"));
                Check(cookieHeader.Contains(".WorkFlow.Identity=") && cookieHeader.Contains("httponly", StringComparison.OrdinalIgnoreCase) &&
                    cookieHeader.Contains("secure", StringComparison.OrdinalIgnoreCase), "password change refreshes a protected authentication cookie");
            }
            using (var db = open())
            {
                var current = await db.Users.AsNoTracking().SingleAsync(u => u.Id == ownerA);
                var hasher = new PasswordHasher<ApplicationUser>();
                Check(current.PasswordHash != hashBefore && current.SecurityStamp != stampBefore &&
                    hasher.VerifyHashedPassword(current, current.PasswordHash!, newSecret) != PasswordVerificationResult.Failed &&
                    hasher.VerifyHashedPassword(current, current.PasswordHash!, secret) == PasswordVerificationResult.Failed,
                    "password change rotates the Identity hash and security stamp");
                Check((await db.Users.AsNoTracking().SingleAsync(u => u.Id == ownerB)).PasswordHash == foreignHash,
                    "forged user and email fields cannot change another account's password");
            }
            using (var response = await a.Client.GetAsync("/Account/Settings"))
                Check(response.IsSuccessStatusCode && WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()).Contains("Parolanız güncellendi."),
                    "refreshed session stays authenticated and sees Turkish success feedback");
            using (var oldLogin = host.CreateSession())
            using (var response = await Post(oldLogin, "/Account/Login", Credentials(emailA, secret)))
                Check(response.StatusCode == HttpStatusCode.OK && oldLogin.Cookies.GetCookies(host.BaseUri)[".WorkFlow.Identity"] is null,
                    "old password no longer signs in");
            using (var newLogin = host.CreateSession())
            {
                using var response = await Post(newLogin, "/Account/Login", Credentials(emailA, newSecret));
                Check(response.StatusCode == HttpStatusCode.Redirect && newLogin.Cookies.GetCookies(host.BaseUri)[".WorkFlow.Identity"] is not null,
                    "new password signs in through the normal login endpoint");
            }
            using (var response = await a.Client.GetAsync("/Account/Logout")) Check(response.StatusCode == HttpStatusCode.MethodNotAllowed, "logout is not available through GET");
            using (var response = await Post(a, "/Account/Logout", new(), csrf: false)) Check(response.StatusCode == HttpStatusCode.BadRequest, "logout rejects missing antiforgery token");
            using (var stillSignedIn = await a.Client.GetAsync("/Tasks")) Check(stillSignedIn.StatusCode == HttpStatusCode.OK, "rejected logout preserves authenticated session");
            using (var response = await Post(a, "/Account/Logout", new(), tokenPath: "/Tasks/Create")) Check(response.StatusCode == HttpStatusCode.Redirect, "valid antiforgery POST signs the user out");
            using (var protectedAfterLogout = await a.Client.GetAsync("/Tasks")) Check(LoginChallenge(protectedAfterLogout), "signed-out session can no longer access tasks");
            if (Environment.GetEnvironmentVariable("WORKFLOW_BROWSER_CHECKS") == "1")
            {
                var start = new ProcessStartInfo("powershell") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(Directory.GetCurrentDirectory(), "Tests", "IdentityBrowserChecks.ps1"), "-BaseUrl", host.BaseUri.ToString().TrimEnd('/') }) start.ArgumentList.Add(argument);
                host.ConfigureBrowser(start, b);
                using var process = Process.Start(start)!; var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync(); var stdout = await output; Console.Write(stdout);
                if (process.ExitCode != 0) throw new InvalidOperationException("Identity browser checks failed: " + await error);
                count += Regex.Matches(stdout, "(?m)^PASS:").Count;
            }
        }
        finally
        {
            using var db = open();
            await db.Tasks.Where(t => t.Title.StartsWith(marker)).ExecuteDeleteAsync();
            await db.Users.Where(u => u.Email == emailA || u.Email == emailB).ExecuteDeleteAsync();
        }
        Check(await Snapshot() == original, "all original tasks preserved after owned authentication fixtures are cleaned");
        using (var db = open()) Check(JsonSerializer.Serialize(await db.Users.AsNoTracking().OrderBy(u => u.Id).ToListAsync()) == originalUsers, "all original Identity users preserved after owned authentication fixtures are cleaned");
        return count;
    }
}
