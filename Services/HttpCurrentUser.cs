using System.Security.Claims;
using WorkFlow.Mvc.Interfaces;

namespace WorkFlow.Mvc.Services;

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string? UserId => accessor.HttpContext?.User.Identity?.IsAuthenticated == true
        ? accessor.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) : null;
}
