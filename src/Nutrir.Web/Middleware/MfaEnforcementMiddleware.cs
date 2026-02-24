using Microsoft.AspNetCore.Identity;
using Nutrir.Core.Entities;

namespace Nutrir.Web.Middleware;

public class MfaEnforcementMiddleware
{
    private readonly RequestDelegate _next;

    public MfaEnforcementMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, UserManager<ApplicationUser> userManager)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path.Value ?? string.Empty;

        // Allow static assets
        if (path.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/_content", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/_blazor", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".css", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".woff2", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".woff", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // Allow MFA setup pages and account management
        if (path.StartsWith("/Account/Manage", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // Allow logout
        if (path.StartsWith("/Account/Logout", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var user = await userManager.GetUserAsync(context.User);
        if (user is null)
        {
            await _next(context);
            return;
        }

        if (!user.TwoFactorEnabled)
        {
            context.Response.Redirect("/Account/Manage/TwoFactorAuthentication");
            return;
        }

        await _next(context);
    }
}
