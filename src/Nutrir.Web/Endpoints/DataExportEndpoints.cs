using System.Security.Claims;
using Nutrir.Core.Interfaces;

namespace Nutrir.Web.Endpoints;

public static class DataExportEndpoints
{
    public static void MapDataExportEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/clients/{clientId:int}/export")
            .RequireAuthorization(policy => policy.RequireRole("Admin", "Nutritionist"))
            .RequireRateLimiting("dataExport");

        group.MapGet("/", async (int clientId, string? format, IDataExportService exportService, HttpContext ctx) =>
        {
            var userId = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
            var exportFormat = (format ?? "pdf").ToLowerInvariant();

            if (exportFormat != "json" && exportFormat != "pdf")
                return Results.BadRequest("Invalid format. Use 'json' or 'pdf'.");

            ctx.Response.Headers.CacheControl = "no-store";

            try
            {
                if (exportFormat == "json")
                {
                    var jsonBytes = await exportService.ExportAsJsonAsync(clientId, userId);
                    return Results.File(jsonBytes, "application/json", $"client-{clientId}-export.json");
                }
                else
                {
                    var pdfBytes = await exportService.ExportAsPdfAsync(clientId, userId);
                    return Results.File(pdfBytes, "application/pdf", $"client-{clientId}-export.pdf");
                }
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound($"Client #{clientId} not found.");
            }
        });
    }
}
