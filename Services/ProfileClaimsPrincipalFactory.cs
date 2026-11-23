using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using WorkFlow.Mvc.Models;

namespace WorkFlow.Mvc.Services;

public sealed class ProfileClaimsPrincipalFactory(
    UserManager<ApplicationUser> users,
    RoleManager<IdentityRole> roles,
    IOptions<IdentityOptions> options) : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>(users, roles, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        if (!string.IsNullOrWhiteSpace(user.DisplayName))
            identity.AddClaim(new Claim("workflow:display-name", user.DisplayName));
        if (user.PhotoKey is Guid key)
            identity.AddClaim(new Claim("workflow:photo", key.ToString("D")));
        return identity;
    }
}
