using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Configuration;
using Nutrir.Infrastructure.Data;
using Nutrir.Infrastructure.Services;

namespace Nutrir.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddPooledDbContextFactory<AppDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));
        // Also register scoped DbContext so existing services can still inject AppDbContext directly
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());

        services.Configure<SeedOptions>(configuration.GetSection(SeedOptions.SectionName));
        services.AddScoped<DatabaseSeeder>();

        services.AddScoped<IAuditSourceProvider, AuditSourceProvider>();
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddScoped<IInviteCodeService, InviteCodeService>();
        services.AddScoped<IUserManagementService, UserManagementService>();
        services.AddScoped<IClientService, ClientService>();
        services.AddScoped<IConsentService, ConsentService>();
        services.Configure<ConsentFormOptions>(configuration.GetSection(ConsentFormOptions.SectionName));
        services.AddScoped<IConsentFormTemplate, DefaultConsentFormTemplate>();
        services.AddScoped<IConsentFormService, ConsentFormService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IAppointmentService, AppointmentService>();
        services.AddScoped<IMealPlanService, MealPlanService>();
        services.AddScoped<IProgressService, ProgressService>();
        services.AddScoped<ISearchService, SearchService>();

        services.AddSingleton<IMaintenanceService, MaintenanceService>();

        // AI Assistant
        services.Configure<AnthropicOptions>(configuration.GetSection(AnthropicOptions.SectionName));
        services.Configure<AiRateLimitOptions>(configuration.GetSection(AiRateLimitOptions.SectionName));
        services.AddScoped<AiToolExecutor>();
        services.AddSingleton<IAiRateLimiter, AiRateLimiter>();
        services.AddScoped<IAiUsageTracker, AiUsageTracker>();
        services.AddScoped<IAiConversationStore, AiConversationStore>();
        services.AddScoped<IAiAgentService, AiAgentService>();

        return services;
    }
}
