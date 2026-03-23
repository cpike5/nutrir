using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Configuration;
using Nutrir.Infrastructure.Data;
using Nutrir.Infrastructure.Security;
using Nutrir.Infrastructure.Services;

namespace Nutrir.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Field-level encryption (must be registered before DbContext factory so
        // the static instance is available when OnModelCreating runs)
        services.Configure<EncryptionOptions>(configuration.GetSection(EncryptionOptions.SectionName));
        services.AddSingleton<AesGcmFieldEncryptor>();
        // Initialize static instance eagerly for EF value converters in pooled DbContext
        var encryptionOptions = new EncryptionOptions();
        configuration.GetSection(EncryptionOptions.SectionName).Bind(encryptionOptions);
        AesGcmFieldEncryptor.Instance = new AesGcmFieldEncryptor(
            Microsoft.Extensions.Options.Options.Create(encryptionOptions),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AesGcmFieldEncryptor>.Instance);

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
        services.AddScoped<IClientHealthProfileService, ClientHealthProfileService>();
        services.AddScoped<IMedicationService, MedicationService>();
        services.AddScoped<IConditionService, ConditionService>();
        services.AddScoped<IConsentService, ConsentService>();
        services.Configure<ConsentFormOptions>(configuration.GetSection(ConsentFormOptions.SectionName));
        services.AddScoped<IConsentFormTemplate, DefaultConsentFormTemplate>();
        services.AddScoped<IConsentFormService, ConsentFormService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<ISessionNoteService, SessionNoteService>();
        services.AddScoped<IAppointmentService, AppointmentService>();
        services.AddScoped<ICalendarService, CalendarService>();
        services.AddScoped<IAvailabilityService, AvailabilityService>();
        services.AddScoped<IMealPlanService, MealPlanService>();
        services.AddScoped<IAllergenCheckService, AllergenCheckService>();
        services.AddScoped<IAllergenService, AllergenService>();
        services.AddScoped<IMealPlanPdfService, MealPlanPdfService>();
        services.AddScoped<IDataExportService, DataExportService>();
        services.AddScoped<IProgressService, ProgressService>();
        services.AddScoped<ISearchService, SearchService>();
        services.AddScoped<IReportService, ReportService>();
        services.Configure<IntakeFormOptions>(configuration.GetSection(IntakeFormOptions.SectionName));
        services.AddScoped<IIntakeFormService, IntakeFormService>();
        services.AddScoped<ITimeZoneService, TimeZoneService>();
        services.AddScoped<IRetentionTracker, RetentionTracker>();
        services.AddScoped<IDataPurgeService, DataPurgeService>();

        services.AddSingleton<IMaintenanceService, MaintenanceService>();

        // AI Assistant
        services.Configure<AnthropicOptions>(configuration.GetSection(AnthropicOptions.SectionName));
        services.Configure<AiRateLimitOptions>(configuration.GetSection(AiRateLimitOptions.SectionName));
        services.AddScoped<AiToolExecutor>();
        services.AddSingleton<IAiRateLimiter, AiRateLimiter>();
        services.AddScoped<IAiUsageTracker, AiUsageTracker>();
        services.AddScoped<IAiConversationStore, AiConversationStore>();
        services.AddScoped<IAiAgentService, AiAgentService>();
        services.AddSingleton<IAiMarkdownRenderer, AiMarkdownRenderer>();
        services.AddSingleton<IAiSuggestionService, AiSuggestionService>();

        // Email
        services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.SectionName));
        services.AddSingleton<IEmailService, EmailService>();

        // Appointment Reminders
        services.AddSingleton<IReminderEmailBuilder, ReminderEmailBuilder>();
        services.AddScoped<IReminderService, ReminderService>();
        services.AddHostedService<ReminderBackgroundService>();

        // Background services
        services.Configure<AutoArchiveOptions>(configuration.GetSection(AutoArchiveOptions.SectionName));
        services.AddHostedService<MealPlanAutoArchiveService>();

        // AI data retention (PIPEDA compliance)
        services.Configure<AiRetentionOptions>(configuration.GetSection(AiRetentionOptions.SectionName));
        services.AddHostedService<AiContentStrippingService>();
        services.AddHostedService<AiConversationPurgeService>();

        // Field encryption data migration (runs once at startup)
        services.AddHostedService<FieldEncryptionMigrationService>();

        return services;
    }
}
