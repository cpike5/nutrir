using System.Security.Claims;
using Nutrir.Core.Interfaces;

namespace Nutrir.Web.Middleware;

public class MaintenanceModeMiddleware
{
    private readonly RequestDelegate _next;

    public MaintenanceModeMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IMaintenanceService maintenanceService)
    {
        var state = maintenanceService.GetState();

        if (!state.IsEnabled)
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path.Value ?? string.Empty;

        // Allow the 503 error page itself
        if (path.StartsWith("/error/503", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // Allow admin maintenance API endpoints
        if (path.StartsWith("/api/admin/maintenance", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

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

        // Allow authenticated admin/nutritionist users through
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var isAdmin = context.User.IsInRole("Admin") || context.User.IsInRole("Nutritionist");
            if (isAdmin)
            {
                await _next(context);
                return;
            }
        }

        // Set Retry-After header if we have an estimated end time
        if (state.EstimatedEndAt.HasValue)
        {
            var retryAfterSeconds = (int)Math.Max(0,
                (state.EstimatedEndAt.Value - DateTime.UtcNow).TotalSeconds);
            context.Response.Headers["Retry-After"] = retryAfterSeconds.ToString();
        }

        context.Response.StatusCode = 503;
        context.Response.Redirect("/error/503");
    }
}
