using System.Security.Claims;
using BEKP.Application.Abstractions;
using Microsoft.AspNetCore.Http;

namespace BEKP.Infrastructure.Security;

/// <summary>Resolves the caller from the authenticated principal; background work is attributed to "system".</summary>
internal sealed class HttpContextCurrentActor(IHttpContextAccessor accessor) : ICurrentActor
{
    public string Name
    {
        get
        {
            var user = accessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
            {
                return "system";
            }

            return user.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? user.FindFirstValue("sub")
                ?? user.Identity.Name
                ?? "unknown";
        }
    }
}
