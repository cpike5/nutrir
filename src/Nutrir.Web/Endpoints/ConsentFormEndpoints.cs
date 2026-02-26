using System.Security.Claims;
using Nutrir.Core.Interfaces;

namespace Nutrir.Web.Endpoints;

public static class ConsentFormEndpoints
{
    public static void MapConsentFormEndpoints(this WebApplication app)
    {
        // Preview endpoints — generate from form data without a persisted client
        var preview = app.MapGroup("/api/consent-form/preview")
            .RequireAuthorization(policy => policy.RequireRole("Admin", "Nutritionist", "Assistant"));

        preview.MapGet("/pdf", (string clientName, string practitionerName, IConsentFormService service) =>
        {
            if (string.IsNullOrWhiteSpace(clientName) || string.IsNullOrWhiteSpace(practitionerName))
                return Results.BadRequest("clientName and practitionerName are required.");

            var pdfBytes = service.GeneratePreviewPdf(clientName, practitionerName);
            return Results.File(pdfBytes, "application/pdf", "consent-form.pdf");
        });

        preview.MapGet("/docx", (string clientName, string practitionerName, IConsentFormService service) =>
        {
            if (string.IsNullOrWhiteSpace(clientName) || string.IsNullOrWhiteSpace(practitionerName))
                return Results.BadRequest("clientName and practitionerName are required.");

            var docxBytes = service.GeneratePreviewDocx(clientName, practitionerName);
            return Results.File(docxBytes,
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                "consent-form.docx");
        });

        // Client-specific endpoints — require a persisted client
        var group = app.MapGroup("/api/clients/{clientId:int}/consent-form")
            .RequireAuthorization(policy => policy.RequireRole("Admin", "Nutritionist", "Assistant"));

        group.MapGet("/pdf", async (int clientId, IConsentFormService service, HttpContext ctx) =>
        {
            var userId = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
            var pdfBytes = await service.GeneratePdfAsync(clientId, userId);

            return Results.File(pdfBytes, "application/pdf", $"consent-form-{clientId}.pdf");
        });

        group.MapGet("/docx", async (int clientId, IConsentFormService service, HttpContext ctx) =>
        {
            var userId = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
            var docxBytes = await service.GenerateDocxAsync(clientId, userId);

            return Results.File(docxBytes,
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                $"consent-form-{clientId}.docx");
        });

        group.MapPost("/sign", async (int clientId, IConsentFormService service, HttpContext ctx) =>
        {
            var userId = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
            var form = await service.MarkPhysicallySignedAsync(clientId, userId);

            return Results.Ok(form);
        });

        group.MapPost("/scan", async (int clientId, IFormFile file, IConsentFormService service, HttpContext ctx) =>
        {
            var userId = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;

            await using var stream = file.OpenReadStream();
            var form = await service.UploadScannedCopyAsync(clientId, stream, file.FileName, userId);

            return form is not null ? Results.Ok(form) : Results.NotFound();
        }).DisableAntiforgery();
    }
}
