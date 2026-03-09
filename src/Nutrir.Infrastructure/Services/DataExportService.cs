using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nutrir.Core.DTOs;
using Nutrir.Core.Entities;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Data;

namespace Nutrir.Infrastructure.Services;

public class DataExportService : IDataExportService
{
    private const string PipedaNotice = "This export contains all personal information and personal health information held about this client, as required under PIPEDA Principle 4.9 (Individual Access).";
    private const string ExportVersion = "1.0";

    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IAuditLogService _auditLogService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<DataExportService> _logger;

    public DataExportService(
        IDbContextFactory<AppDbContext> dbContextFactory,
        IAuditLogService auditLogService,
        UserManager<ApplicationUser> userManager,
        ILogger<DataExportService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _auditLogService = auditLogService;
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<ClientDataExportDto> CollectClientDataAsync(int clientId, string userId, string format = "json")
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        // Get the client
        var client = await db.Clients.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == clientId);
        if (client is null)
            throw new KeyNotFoundException($"Client #{clientId} not found.");

        // Get the current user for generating user name
        var generatingUser = await _userManager.FindByIdAsync(userId);
        var generatingUserName = generatingUser?.DisplayName ?? userId;

        // Get primary nutritionist name
        var primaryNutritionist = await db.Set<ApplicationUser>()
            .FirstOrDefaultAsync(u => u.Id == client.PrimaryNutritionistId);
        var primaryNutritionistName = primaryNutritionist?.DisplayName ?? client.PrimaryNutritionistId;

        // Collect all user IDs we'll need for batch resolution
        var userIds = new HashSet<string> { client.PrimaryNutritionistId };

        // Collect health profile
        var allergies = await db.ClientAllergies.IgnoreQueryFilters()
            .Where(a => a.ClientId == clientId)
            .ToListAsync();

        var medications = await db.ClientMedications.IgnoreQueryFilters()
            .Where(m => m.ClientId == clientId)
            .ToListAsync();

        var conditions = await db.ClientConditions.IgnoreQueryFilters()
            .Where(c => c.ClientId == clientId)
            .ToListAsync();

        var dietaryRestrictions = await db.ClientDietaryRestrictions.IgnoreQueryFilters()
            .Where(dr => dr.ClientId == clientId)
            .ToListAsync();

        var healthProfile = new HealthProfileExportDto(
            Allergies: allergies.Select(a => new AllergyExportDto(
                a.Name,
                a.Severity.ToString(),
                a.AllergyType.ToString(),
                a.IsDeleted,
                a.DeletedAt
            )).ToList(),
            Medications: medications.Select(m => new MedicationExportDto(
                m.Name,
                m.Dosage,
                m.Frequency,
                m.PrescribedFor,
                m.IsDeleted,
                m.DeletedAt
            )).ToList(),
            Conditions: conditions.Select(c => new ConditionExportDto(
                c.Name,
                c.Code,
                c.DiagnosisDate,
                c.Status.ToString(),
                c.Notes,
                c.IsDeleted,
                c.DeletedAt
            )).ToList(),
            DietaryRestrictions: dietaryRestrictions.Select(dr => new DietaryRestrictionExportDto(
                dr.RestrictionType.ToString(),
                dr.Notes,
                dr.IsDeleted,
                dr.DeletedAt
            )).ToList()
        );

        // Collect appointments
        var appointments = await db.Appointments.IgnoreQueryFilters()
            .Where(a => a.ClientId == clientId)
            .ToListAsync();

        var appointmentUserIds = appointments.Select(a => a.NutritionistId).Distinct();
        foreach (var appUserId in appointmentUserIds)
            userIds.Add(appUserId);

        // Collect meal plans with full hierarchy
        var mealPlans = await db.MealPlans.IgnoreQueryFilters()
            .Where(mp => mp.ClientId == clientId)
            .Include(mp => mp.Days)
                .ThenInclude(d => d.MealSlots)
                .ThenInclude(s => s.Items)
            .Include(mp => mp.AllergenWarningOverrides)
            .ToListAsync();

        var mealPlanUserIds = mealPlans.Select(mp => mp.CreatedByUserId).Distinct();
        foreach (var mpUserId in mealPlanUserIds)
            userIds.Add(mpUserId);

        // Collect progress goals
        var progressGoals = await db.ProgressGoals.IgnoreQueryFilters()
            .Where(g => g.ClientId == clientId)
            .ToListAsync();

        var goalUserIds = progressGoals.Select(g => g.CreatedByUserId).Distinct();
        foreach (var gUserId in goalUserIds)
            userIds.Add(gUserId);

        // Collect progress entries with measurements
        var progressEntries = await db.ProgressEntries.IgnoreQueryFilters()
            .Where(e => e.ClientId == clientId)
            .Include(e => e.Measurements)
            .ToListAsync();

        var entryUserIds = progressEntries.Select(e => e.CreatedByUserId).Distinct();
        foreach (var eUserId in entryUserIds)
            userIds.Add(eUserId);

        // Collect intake forms with responses
        var intakeForms = await db.IntakeForms.IgnoreQueryFilters()
            .Where(f => f.ClientId == clientId)
            .Include(f => f.Responses)
            .ToListAsync();

        var formUserIds = intakeForms
            .Select(f => f.CreatedByUserId)
            .Concat(intakeForms.Where(f => f.ReviewedByUserId != null).Select(f => f.ReviewedByUserId!))
            .Distinct();
        foreach (var fUserId in formUserIds)
            userIds.Add(fUserId);

        // Collect consent events and forms (IgnoreQueryFilters for consistency,
        // even though these entities are append-only with no soft-delete)
        var consentEvents = await db.ConsentEvents.IgnoreQueryFilters()
            .Where(ce => ce.ClientId == clientId)
            .ToListAsync();

        var consentForms = await db.ConsentForms.IgnoreQueryFilters()
            .Where(cf => cf.ClientId == clientId)
            .ToListAsync();

        var consentUserIds = consentEvents
            .Select(ce => ce.RecordedByUserId)
            .Concat(consentForms.Select(cf => cf.GeneratedByUserId))
            .Concat(consentForms.Where(cf => cf.SignedByUserId != null).Select(cf => cf.SignedByUserId!))
            .Distinct();
        foreach (var cUserId in consentUserIds)
            userIds.Add(cUserId);

        // Batch-resolve all user display names
        var userNameMap = await db.Set<ApplicationUser>()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName })
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName);

        // Helper to get display name safely
        string GetUserName(string userId) =>
            userNameMap.TryGetValue(userId, out var name) ? name ?? userId : userId;

        // Convert appointments to export DTOs
        var appointmentExports = appointments.Select(a => new AppointmentExportDto(
            Type: a.Type.ToString(),
            Status: a.Status.ToString(),
            StartTime: a.StartTime,
            DurationMinutes: a.DurationMinutes,
            Location: a.Location.ToString(),
            LocationNotes: a.LocationNotes,
            Notes: a.Notes,
            NutritionistName: GetUserName(a.NutritionistId),
            CancellationReason: a.CancellationReason,
            CancelledAt: a.CancelledAt,
            IsDeleted: a.IsDeleted,
            DeletedAt: a.DeletedAt,
            CreatedAt: a.CreatedAt
        )).ToList();

        // Convert meal plans to export DTOs
        var mealPlanExports = mealPlans.Select(mp => new MealPlanExportDto(
            Title: mp.Title,
            Description: mp.Description,
            Status: mp.Status.ToString(),
            StartDate: mp.StartDate,
            EndDate: mp.EndDate,
            CalorieTarget: mp.CalorieTarget,
            ProteinTargetG: mp.ProteinTargetG,
            CarbsTargetG: mp.CarbsTargetG,
            FatTargetG: mp.FatTargetG,
            Notes: mp.Notes,
            Instructions: mp.Instructions,
            CreatedByName: GetUserName(mp.CreatedByUserId),
            Days: mp.Days.OrderBy(d => d.DayNumber).Select(day => new MealPlanDayExportDto(
                DayNumber: day.DayNumber,
                Label: day.Label,
                Notes: day.Notes,
                MealSlots: day.MealSlots.OrderBy(s => s.SortOrder).Select(slot => new MealSlotExportDto(
                    MealType: slot.MealType.ToString(),
                    CustomName: slot.CustomName,
                    Notes: slot.Notes,
                    Items: slot.Items.OrderBy(i => i.SortOrder).Select(item => new MealItemExportDto(
                        FoodName: item.FoodName,
                        Quantity: item.Quantity,
                        Unit: item.Unit,
                        CaloriesKcal: item.CaloriesKcal,
                        ProteinG: item.ProteinG,
                        CarbsG: item.CarbsG,
                        FatG: item.FatG,
                        Notes: item.Notes
                    )).ToList()
                )).ToList()
            )).ToList(),
            IsDeleted: mp.IsDeleted,
            DeletedAt: mp.DeletedAt,
            CreatedAt: mp.CreatedAt
        )).ToList();

        // Convert progress goals to export DTOs
        var progressGoalExports = progressGoals.Select(g => new ProgressGoalExportDto(
            Title: g.Title,
            Description: g.Description,
            GoalType: g.GoalType.ToString(),
            TargetValue: g.TargetValue,
            TargetUnit: g.TargetUnit,
            TargetDate: g.TargetDate,
            Status: g.Status.ToString(),
            CreatedByName: GetUserName(g.CreatedByUserId),
            IsDeleted: g.IsDeleted,
            DeletedAt: g.DeletedAt,
            CreatedAt: g.CreatedAt
        )).ToList();

        // Convert progress entries to export DTOs
        var progressEntryExports = progressEntries.Select(e => new ProgressEntryExportDto(
            EntryDate: e.EntryDate,
            Notes: e.Notes,
            CreatedByName: GetUserName(e.CreatedByUserId),
            Measurements: e.Measurements.Select(m => new ProgressMeasurementExportDto(
                MetricType: m.MetricType.ToString(),
                CustomMetricName: m.CustomMetricName,
                Value: m.Value,
                Unit: m.Unit
            )).ToList(),
            IsDeleted: e.IsDeleted,
            DeletedAt: e.DeletedAt,
            CreatedAt: e.CreatedAt
        )).ToList();

        // Convert intake forms to export DTOs
        var intakeFormExports = intakeForms.Select(f => new IntakeFormExportDto(
            Status: f.Status.ToString(),
            SubmittedAt: f.SubmittedAt,
            ReviewedAt: f.ReviewedAt,
            ReviewedByName: f.ReviewedByUserId != null ? GetUserName(f.ReviewedByUserId) : null,
            CreatedByName: GetUserName(f.CreatedByUserId),
            Responses: f.Responses.Select(r => new IntakeFormResponseExportDto(
                SectionKey: r.SectionKey,
                FieldKey: r.FieldKey,
                Value: r.Value
            )).ToList(),
            IsDeleted: f.IsDeleted,
            DeletedAt: f.DeletedAt,
            CreatedAt: f.CreatedAt
        )).ToList();

        // Convert consent events to export DTOs
        var consentEventExports = consentEvents.Select(ce => new ConsentEventExportDto(
            EventType: ce.EventType.ToString(),
            ConsentPurpose: ce.ConsentPurpose,
            PolicyVersion: ce.PolicyVersion,
            Timestamp: ce.Timestamp,
            RecordedByName: GetUserName(ce.RecordedByUserId),
            Notes: ce.Notes
        )).ToList();

        // Convert consent forms to export DTOs
        var consentFormExports = consentForms.Select(cf => new ConsentFormExportDto(
            FormVersion: cf.FormVersion,
            GeneratedAt: cf.GeneratedAt,
            GeneratedByName: GetUserName(cf.GeneratedByUserId),
            SignatureMethod: cf.SignatureMethod.ToString(),
            IsSigned: cf.IsSigned,
            SignedAt: cf.SignedAt,
            SignedByName: cf.SignedByUserId != null ? GetUserName(cf.SignedByUserId) : null,
            Notes: cf.Notes,
            CreatedAt: cf.CreatedAt
        )).ToList();

        // Collect audit log entries - two-pass approach
        var clientAuditEntries = await db.AuditLogEntries
            .Where(e => e.EntityType == "Client" && e.EntityId == clientId.ToString())
            .ToListAsync();

        // Collect typed sub-entity ID sets for precise audit log querying
        var appointmentIds = appointments.Select(a => a.Id.ToString()).ToHashSet();
        var mealPlanIds = mealPlans.Select(mp => mp.Id.ToString()).ToHashSet();
        var progressGoalIds = progressGoals.Select(g => g.Id.ToString()).ToHashSet();
        var progressEntryIds = progressEntries.Select(e => e.Id.ToString()).ToHashSet();
        var intakeFormIds = intakeForms.Select(f => f.Id.ToString()).ToHashSet();

        // Query for audit entries matching specific entity type + ID pairs
        var subAuditEntries = await db.AuditLogEntries
            .Where(e =>
                (e.EntityType == "Appointment" && appointmentIds.Contains(e.EntityId!)) ||
                (e.EntityType == "MealPlan" && mealPlanIds.Contains(e.EntityId!)) ||
                (e.EntityType == "ProgressGoal" && progressGoalIds.Contains(e.EntityId!)) ||
                (e.EntityType == "ProgressEntry" && progressEntryIds.Contains(e.EntityId!)) ||
                (e.EntityType == "IntakeForm" && intakeFormIds.Contains(e.EntityId!)))
            .ToListAsync();

        var allAuditEntries = clientAuditEntries.Concat(subAuditEntries).ToList();

        var auditLogExports = allAuditEntries.Select(e => new AuditLogExportDto(
            Timestamp: e.Timestamp,
            Action: e.Action,
            EntityType: e.EntityType,
            EntityId: e.EntityId,
            Details: e.Details,
            Source: e.Source.ToString()
        )).ToList();

        var consentHistory = new ConsentHistoryExportDto(
            Events: consentEventExports,
            Forms: consentFormExports
        );

        var clientProfile = new ClientProfileExportDto(
            FirstName: client.FirstName,
            LastName: client.LastName,
            Email: client.Email,
            Phone: client.Phone,
            DateOfBirth: client.DateOfBirth,
            Notes: client.Notes,
            ConsentGiven: client.ConsentGiven,
            ConsentTimestamp: client.ConsentTimestamp,
            ConsentPolicyVersion: client.ConsentPolicyVersion,
            PrimaryNutritionistName: primaryNutritionistName,
            IsDeleted: client.IsDeleted,
            CreatedAt: client.CreatedAt,
            UpdatedAt: client.UpdatedAt,
            DeletedAt: client.DeletedAt
        );

        var metadata = new ExportMetadataDto(
            ExportDate: DateTime.UtcNow,
            ExportVersion: ExportVersion,
            ExportFormat: format,
            ClientId: clientId,
            GeneratedByName: generatingUserName,
            PipedaNotice: PipedaNotice
        );

        var export = new ClientDataExportDto(
            ExportMetadata: metadata,
            ClientProfile: clientProfile,
            HealthProfile: healthProfile,
            Appointments: appointmentExports,
            MealPlans: mealPlanExports,
            ProgressGoals: progressGoalExports,
            ProgressEntries: progressEntryExports,
            IntakeForms: intakeFormExports,
            ConsentHistory: consentHistory,
            AuditLog: auditLogExports
        );

        _logger.LogInformation("Client data export collected for client {ClientId} by user {UserId}", clientId, userId);

        return export;
    }

    public async Task<byte[]> ExportAsJsonAsync(int clientId, string userId)
    {
        var data = await CollectClientDataAsync(clientId, userId, "json");

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var json = JsonSerializer.Serialize(data, options);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);

        await _auditLogService.LogAsync(
            userId,
            "ClientDataExported",
            "Client",
            clientId.ToString(),
            "Exported as JSON");

        _logger.LogInformation("Client data exported as JSON for client {ClientId} by user {UserId}", clientId, userId);

        return bytes;
    }

    public async Task<byte[]> ExportAsPdfAsync(int clientId, string userId)
    {
        var data = await CollectClientDataAsync(clientId, userId, "pdf");
        var pdfBytes = DataExportPdfRenderer.Render(data);

        await _auditLogService.LogAsync(
            userId,
            "ClientDataExported",
            "Client",
            clientId.ToString(),
            "Exported as PDF");

        _logger.LogInformation("Client data exported as PDF for client {ClientId} by user {UserId}", clientId, userId);

        return pdfBytes;
    }
}
