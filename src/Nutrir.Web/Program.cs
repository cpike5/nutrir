using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Nutrir.Core.Entities;
using Nutrir.Infrastructure;
using Nutrir.Infrastructure.Data;
using Nutrir.Core.Interfaces;
using Nutrir.Web.Components;
using Nutrir.Web.Components.Account;
using Nutrir.Web.Middleware;
using Elastic.Apm.SerilogEnricher;
using Elastic.Serilog.Sinks;
using Elastic.Transport;
using Nutrir.Web.Endpoints;
using Nutrir.Web.Hubs;
using Nutrir.Web.Services;
using Radzen;
using QuestPDF.Infrastructure;
using Serilog;
using System.Threading.RateLimiting;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) =>
    {
        configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext();

        if (!string.IsNullOrEmpty(context.Configuration["ElasticApm:ServerUrl"]))
            configuration.Enrich.WithElasticApmCorrelationInfo();

        var elasticsearchUrl = context.Configuration["Elasticsearch:Url"];
        if (!string.IsNullOrEmpty(elasticsearchUrl))
        {
            var apiKey = context.Configuration["Elasticsearch:ApiKey"];
            var nodes = new[] { new Uri(elasticsearchUrl) };
            configuration.WriteTo.Elasticsearch(nodes,
                opts =>
                {
                    opts.DataStream = new Elastic.Ingest.Elasticsearch.DataStreams.DataStreamName("logs", "nutrir");
                    opts.BootstrapMethod = Elastic.Ingest.Elasticsearch.BootstrapMethod.Failure;
                },
                transport =>
                {
                    if (!string.IsNullOrEmpty(apiKey))
                        transport.Authentication(new ApiKey(apiKey));
                });
        }
    });

    // Add services to the container.
    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents();

    builder.Services.AddCascadingAuthenticationState();
    builder.Services.AddScoped<IdentityUserAccessor>();
    builder.Services.AddScoped<IdentityRedirectManager>();
    builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

    builder.Services.AddAuthentication(options =>
        {
            options.DefaultScheme = IdentityConstants.ApplicationScheme;
            options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
        })
        .AddIdentityCookies();

    var apmServerUrl = builder.Configuration["ElasticApm:ServerUrl"];
    if (!string.IsNullOrEmpty(apmServerUrl))
        builder.Services.AddAllElasticApm();

    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddRadzenComponents();
    builder.Services.AddScoped<Nutrir.Web.Components.Layout.AiPanelState>();
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddSingleton<NotificationBroadcaster>();
    builder.Services.AddScoped<RealTimeNotificationService>();
    builder.Services.AddSignalR();
    builder.Services.AddSingleton<INotificationDispatcher, NotificationDispatcher>();
    builder.Services.AddDatabaseDeveloperPageExceptionFilter();

    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddPolicy("dataExport", httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                    ?? httpContext.Connection.RemoteIpAddress?.ToString()
                    ?? "anonymous",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 5,
                    Window = TimeSpan.FromMinutes(15),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0
                }));
    });

    builder.Services.AddIdentityCore<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = true)
        .AddRoles<IdentityRole>()
        .AddEntityFrameworkStores<AppDbContext>()
        .AddSignInManager()
        .AddDefaultTokenProviders();

    builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

    builder.Services.ConfigureApplicationCookie(options =>
    {
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

    builder.Services.AddHsts(options =>
    {
        options.MaxAge = TimeSpan.FromDays(365);
        options.IncludeSubDomains = true;
        options.Preload = true;
    });

    QuestPDF.Settings.License = LicenseType.Community;

    // Set absolute paths for consent form options
    builder.Services.PostConfigure<Nutrir.Infrastructure.Configuration.ConsentFormOptions>(options =>
    {
        options.DocxTemplatePath = Path.Combine(builder.Environment.ContentRootPath, "wwwroot", "templates", "consent-form-template.docx");
        if (!Path.IsPathRooted(options.ScannedCopyStoragePath))
        {
            options.ScannedCopyStoragePath = Path.Combine(builder.Environment.ContentRootPath, options.ScannedCopyStoragePath);
        }
    });

    var app = builder.Build();

    // Apply pending migrations and seed roles/admin user on startup.
    using (var scope = app.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.MigrateAsync();

        var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
        await seeder.SeedAsync(app.Environment.IsDevelopment());
    }

    var configuredPalette = app.Configuration["Theme:Palette"];
    if (!string.IsNullOrWhiteSpace(configuredPalette))
    {
        var knownPalettes = new[] { "sage", "pink", "pink-deep", "pink-soft", "pink-mauve", "pink-lilac" };
        if (knownPalettes.Contains(configuredPalette, StringComparer.OrdinalIgnoreCase))
            Log.Information("Theme palette configured: {Palette}", configuredPalette);
        else
            Log.Warning("Unknown Theme:Palette value '{Palette}'. Known palettes: {Known}. Falling back to default.",
                configuredPalette, string.Join(", ", knownPalettes));
    }

    app.UseSerilogRequestLogging();

    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment())
    {
        app.UseMigrationsEndPoint();
    }
    else
    {
        app.UseHsts();
    }

    app.UseExceptionHandler("/error/500");

    app.UseHttpsRedirection();

    app.UseMiddleware<MfaEnforcementMiddleware>();
    app.UseMiddleware<MaintenanceModeMiddleware>();

    app.UseStatusCodePagesWithReExecute("/error/{0}");

    app.UseAntiforgery();

    app.UseRateLimiter();

    app.MapStaticAssets();
    app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode();

    // Add additional endpoints required by the Identity /Account Razor components.
    app.MapAdditionalIdentityEndpoints();

    // Consent form API endpoints.
    app.MapConsentFormEndpoints();

    // Meal plan API endpoints.
    app.MapMealPlanEndpoints();

    // Data export API endpoints (PIPEDA compliance).
    app.MapDataExportEndpoints();

    // SignalR hub for real-time notifications.
    app.MapHub<NutrirHub>("/hubs/nutrir");

    // Maintenance mode admin API endpoints.
    app.MapGet("/api/admin/maintenance/status", (IMaintenanceService svc) =>
        Results.Ok(svc.GetState()));

    app.MapPost("/api/admin/maintenance/enable", (MaintenanceRequest request, IMaintenanceService svc, HttpContext ctx) =>
    {
        var userName = ctx.User.Identity?.Name ?? "unknown";
        svc.Enable(request.Message, request.EstimatedMinutes, userName);
        return Results.Ok(svc.GetState());
    }).RequireAuthorization(policy => policy.RequireRole("Admin", "Nutritionist"));

    app.MapPost("/api/admin/maintenance/disable", (IMaintenanceService svc) =>
    {
        svc.Disable();
        return Results.Ok(svc.GetState());
    }).RequireAuthorization(policy => policy.RequireRole("Admin", "Nutritionist"));

    // Dev-only endpoints for error page testing.
    if (app.Environment.IsDevelopment())
    {
        app.MapGet("/dev/status/{code:int}", (int code) => Results.StatusCode(code));
        app.MapGet("/dev/throw", void () =>
        {
            throw new InvalidOperationException("Dev Tools: intentional unhandled exception for error page testing.");
        });
    }

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

public record MaintenanceRequest(string? Message = null, int? EstimatedMinutes = null);
