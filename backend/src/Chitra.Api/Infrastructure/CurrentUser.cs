using System.Security.Claims;
using Chitra.Application.Abstractions;
using Chitra.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Chitra.Api.Infrastructure;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>"None" (single local user, default) or "Jwt" (any OpenID Connect issuer: Entra ID, Google, Auth0, ...).</summary>
    public string Mode { get; set; } = "None";
    public string? Authority { get; set; }
    public string? Audience { get; set; }

    public bool Enabled => Mode.Equals("Jwt", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Resolves the current user. Without authentication everything belongs to the built-in local user.</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    internal const string ItemKey = "chitra.userId";

    public Guid UserId =>
        accessor.HttpContext?.Items[ItemKey] is Guid id ? id : User.LocalUserId;

    public string DisplayName =>
        accessor.HttpContext?.User.FindFirstValue("name") ?? accessor.HttpContext?.User.Identity?.Name ?? "Local User";
}

/// <summary>When JWT auth is on, maps the token subject to a local user row (created on first sign-in).</summary>
public sealed class UserProvisioningMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IAppDbContext db)
    {
        var principal = context.User;
        if (principal.Identity?.IsAuthenticated == true)
        {
            var externalId = principal.FindFirstValue("oid") ?? principal.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier")
                             ?? principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
            if (externalId is not null)
            {
                var user = await db.Users.FirstOrDefaultAsync(u => u.ExternalId == externalId, context.RequestAborted);
                if (user is null)
                {
                    user = new User
                    {
                        ExternalId = externalId,
                        DisplayName = principal.FindFirstValue("name") ?? principal.FindFirstValue(ClaimTypes.Email) ?? "User",
                        Email = principal.FindFirstValue(ClaimTypes.Email) ?? principal.FindFirstValue("email"),
                    };
                    db.Users.Add(user);
                    await db.SaveChangesAsync(context.RequestAborted);
                }

                context.Items[HttpCurrentUser.ItemKey] = user.Id;
            }
        }

        await next(context);
    }
}
