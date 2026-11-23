using Microsoft.AspNetCore.Identity;

namespace WorkFlow.Mvc.Models;

public sealed class ApplicationUser : IdentityUser
{
    public string? DisplayName { get; set; }
    public string? AboutMe { get; set; }
    public DateTimeOffset? JoinedAtUtc { get; set; }
    public Guid? PhotoKey { get; set; }
    public byte[]? PhotoData { get; set; }
}
