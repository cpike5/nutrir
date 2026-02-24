using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Data;
using Nutrir.Infrastructure.Services;

namespace Nutrir.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));

        services.Configure<SeedOptions>(configuration.GetSection(SeedOptions.SectionName));
        services.AddScoped<DatabaseSeeder>();

        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddScoped<IInviteCodeService, InviteCodeService>();
        services.AddScoped<IUserManagementService, UserManagementService>();
        services.AddScoped<IClientService, ClientService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IAppointmentService, AppointmentService>();
        services.AddScoped<IMealPlanService, MealPlanService>();
        services.AddScoped<IProgressService, ProgressService>();
        services.AddScoped<ISearchService, SearchService>();

        services.AddSingleton<IMaintenanceService, MaintenanceService>();

        return services;
    }
}
