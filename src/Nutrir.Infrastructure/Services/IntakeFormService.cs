using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nutrir.Core.DTOs;
using Nutrir.Core.Entities;
using Nutrir.Core.Enums;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Configuration;
using Nutrir.Infrastructure.Data;

namespace Nutrir.Infrastructure.Services;

public class IntakeFormService : IIntakeFormService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IAuditLogService _auditLogService;
    private readonly IConsentService _consentService;
    private readonly INotificationDispatcher _notificationDispatcher;
    private readonly IntakeFormOptions _options;
    private readonly ILogger<IntakeFormService> _logger;

    public IntakeFormService(
        IDbContextFactory<AppDbContext> dbContextFactory,
        IAuditLogService auditLogService,
        IConsentService consentService,
        INotificationDispatcher notificationDispatcher,
        IOptions<IntakeFormOptions> options,
        ILogger<IntakeFormService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _auditLogService = auditLogService;
        _consentService = consentService;
        _notificationDispatcher = notificationDispatcher;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IntakeFormDto> CreateFormAsync(string clientEmail, int? appointmentId, int? clientId, string createdByUserId)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var token = GenerateToken();

        var entity = new IntakeForm
        {
            ClientId = clientId,
            AppointmentId = appointmentId,
            Token = token,
            Status = IntakeFormStatus.Pending,
            ClientEmail = clientEmail,
            ExpiresAt = DateTime.UtcNow.AddDays(_options.ExpiryDays),
            CreatedByUserId = createdByUserId,
            CreatedAt = DateTime.UtcNow
        };

        db.IntakeForms.Add(entity);
        await db.SaveChangesAsync();

        _logger.LogInformation(
            "Intake form created: {FormId} for {Email} by {UserId}",
            entity.Id, clientEmail, createdByUserId);

        await _auditLogService.LogAsync(
            createdByUserId,
            "IntakeFormCreated",
            "IntakeForm",
            entity.Id.ToString(),
            $"Created intake form for {clientEmail}");

        await TryDispatchAsync("IntakeForm", entity.Id, EntityChangeType.Created, createdByUserId);

        return MapToDto(entity);
    }

    public async Task<IntakeFormDto?> GetByTokenAsync(string token)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var entity = await db.IntakeForms
            .Include(f => f.Responses)
            .FirstOrDefaultAsync(f => f.Token == token);

        return entity is null ? null : MapToDto(entity);
    }

    public async Task<IntakeFormDto?> GetByIdAsync(int formId)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var entity = await db.IntakeForms
            .Include(f => f.Responses)
            .FirstOrDefaultAsync(f => f.Id == formId);

        return entity is null ? null : MapToDto(entity);
    }

    public async Task<IntakeFormListDto?> GetByAppointmentIdAsync(int appointmentId)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var entity = await db.IntakeForms
            .FirstOrDefaultAsync(f => f.AppointmentId == appointmentId);

        if (entity is null) return null;

        string? clientName = null;
        if (entity.ClientId.HasValue)
        {
            clientName = await db.Clients
                .Where(c => c.Id == entity.ClientId.Value)
                .Select(c => c.FirstName + " " + c.LastName)
                .FirstOrDefaultAsync();
        }

        return new IntakeFormListDto(
            entity.Id,
            entity.ClientId,
            clientName,
            entity.AppointmentId,
            entity.Status,
            entity.ClientEmail,
            entity.ExpiresAt,
            entity.SubmittedAt,
            entity.ReviewedAt,
            entity.CreatedAt);
    }

    public async Task<List<IntakeFormListDto>> ListFormsAsync(IntakeFormStatus? statusFilter = null)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var query = db.IntakeForms.AsQueryable();

        if (statusFilter.HasValue)
        {
            query = query.Where(f => f.Status == statusFilter.Value);
        }

        var forms = await query
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync();

        var clientIds = forms.Where(f => f.ClientId.HasValue).Select(f => f.ClientId!.Value).Distinct().ToList();
        var clientNames = clientIds.Count > 0
            ? await db.Clients
                .Where(c => clientIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => $"{c.FirstName} {c.LastName}")
            : new Dictionary<int, string>();

        return forms.Select(f => new IntakeFormListDto(
            f.Id,
            f.ClientId,
            f.ClientId.HasValue ? clientNames.GetValueOrDefault(f.ClientId.Value) : null,
            f.AppointmentId,
            f.Status,
            f.ClientEmail,
            f.ExpiresAt,
            f.SubmittedAt,
            f.ReviewedAt,
            f.CreatedAt)).ToList();
    }

    public async Task<(bool Success, string? Error)> SubmitFormAsync(string token, List<IntakeFormResponseDto> responses)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var entity = await db.IntakeForms
            .Include(f => f.Responses)
            .FirstOrDefaultAsync(f => f.Token == token);

        if (entity is null)
            return (false, "Intake form not found.");

        if (entity.Status == IntakeFormStatus.Submitted || entity.Status == IntakeFormStatus.Reviewed)
            return (false, "This form has already been submitted.");

        if (entity.ExpiresAt <= DateTime.UtcNow)
        {
            entity.Status = IntakeFormStatus.Expired;
            entity.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return (false, "This intake form link has expired. Please contact your nutritionist for a new link.");
        }

        entity.Responses.Clear();
        foreach (var response in responses)
        {
            entity.Responses.Add(new IntakeFormResponse
            {
                SectionKey = response.SectionKey,
                FieldKey = response.FieldKey,
                Value = response.Value
            });
        }

        entity.Status = IntakeFormStatus.Submitted;
        entity.SubmittedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        _logger.LogInformation("Intake form submitted: {FormId}", entity.Id);

        await _auditLogService.LogAsync(
            "system",
            "IntakeFormSubmitted",
            "IntakeForm",
            entity.Id.ToString(),
            $"Client submitted intake form ({responses.Count} responses)");

        await TryDispatchAsync("IntakeForm", entity.Id, EntityChangeType.Updated, entity.CreatedByUserId);

        return (true, null);
    }

    public async Task<(bool Success, int? ClientId, string? Error)> ReviewFormAsync(int formId, string reviewedByUserId)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var entity = await db.IntakeForms
            .Include(f => f.Responses)
            .FirstOrDefaultAsync(f => f.Id == formId);

        if (entity is null)
            return (false, null, "Intake form not found.");

        if (entity.Status != IntakeFormStatus.Submitted)
            return (false, null, "Only submitted forms can be reviewed.");

        var responses = entity.Responses
            .ToDictionary(r => $"{r.SectionKey}.{r.FieldKey}", r => r.Value);

        var clientId = await MapResponsesToClientAsync(db, entity, responses, reviewedByUserId);

        entity.Status = IntakeFormStatus.Reviewed;
        entity.ReviewedAt = DateTime.UtcNow;
        entity.ReviewedByUserId = reviewedByUserId;
        entity.ClientId = clientId;
        entity.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        _logger.LogInformation(
            "Intake form reviewed: {FormId} → Client {ClientId} by {UserId}",
            entity.Id, clientId, reviewedByUserId);

        await _auditLogService.LogAsync(
            reviewedByUserId,
            "IntakeFormReviewed",
            "IntakeForm",
            entity.Id.ToString(),
            $"Reviewed intake form and mapped to client {clientId}");

        await TryDispatchAsync("IntakeForm", entity.Id, EntityChangeType.Updated, reviewedByUserId);

        return (true, clientId, null);
    }

    private async Task<int> MapResponsesToClientAsync(AppDbContext db, IntakeForm form, Dictionary<string, string> responses, string userId)
    {
        Client client;
        var isNewClient = !form.ClientId.HasValue;

        if (form.ClientId.HasValue)
        {
            client = await db.Clients.FindAsync(form.ClientId.Value)
                     ?? throw new InvalidOperationException($"Client {form.ClientId.Value} not found.");
        }
        else
        {
            client = new Client
            {
                PrimaryNutritionistId = userId,
                CreatedAt = DateTime.UtcNow
            };
            db.Clients.Add(client);
        }

        // Map personal info
        if (responses.TryGetValue("personal_info.first_name", out var firstName))
            client.FirstName = firstName;
        if (responses.TryGetValue("personal_info.last_name", out var lastName))
            client.LastName = lastName;
        if (responses.TryGetValue("personal_info.email", out var email))
            client.Email = email;
        if (responses.TryGetValue("personal_info.phone", out var phone))
            client.Phone = phone;
        if (responses.TryGetValue("personal_info.date_of_birth", out var dob) && DateOnly.TryParse(dob, out var dobValue))
            client.DateOfBirth = dobValue;

        // Map consent
        var consentGiven = responses.TryGetValue("consent.consent_given", out var consent) &&
                           bool.TryParse(consent, out var consentValue) && consentValue;
        if (consentGiven)
        {
            client.ConsentGiven = true;
            client.ConsentTimestamp = DateTime.UtcNow;
            client.ConsentPolicyVersion = responses.GetValueOrDefault("consent.consent_policy_version", _options.ConsentPolicyVersion);
        }

        // Collect notes from various sections
        var notes = new List<string>();
        if (responses.TryGetValue("medical_history.surgeries", out var surgeries) && !string.IsNullOrWhiteSpace(surgeries))
            notes.Add($"Surgeries: {surgeries}");
        if (responses.TryGetValue("medical_history.family_history", out var familyHistory) && !string.IsNullOrWhiteSpace(familyHistory))
            notes.Add($"Family history: {familyHistory}");
        if (responses.TryGetValue("medical_history.additional_notes", out var medNotes) && !string.IsNullOrWhiteSpace(medNotes))
            notes.Add($"Medical notes: {medNotes}");
        if (responses.TryGetValue("goals.specific_concerns", out var concerns) && !string.IsNullOrWhiteSpace(concerns))
            notes.Add($"Specific concerns: {concerns}");
        if (responses.TryGetValue("goals.timeline_expectations", out var timeline) && !string.IsNullOrWhiteSpace(timeline))
            notes.Add($"Timeline expectations: {timeline}");
        if (notes.Count > 0)
            client.Notes = string.Join("\n\n", notes);

        client.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // Map health profile sub-entities
        await MapAllergiesToClientAsync(db, client.Id, responses);
        await MapMedicationsToClientAsync(db, client.Id, responses);
        await MapConditionsToClientAsync(db, client.Id, responses);
        await MapDietaryRestrictionsToClientAsync(db, client.Id, responses);

        if (isNewClient && consentGiven)
        {
            await _consentService.GrantConsentAsync(
                client.Id,
                "Treatment and care",
                client.ConsentPolicyVersion ?? _options.ConsentPolicyVersion,
                userId);
        }

        return client.Id;
    }

    private async Task MapAllergiesToClientAsync(AppDbContext db, int clientId, Dictionary<string, string> responses)
    {
        if (!responses.TryGetValue("medical_history.allergies", out var allergiesJson))
            return;

        try
        {
            var allergies = JsonSerializer.Deserialize<List<string>>(allergiesJson);
            if (allergies is null) return;

            foreach (var allergyName in allergies.Where(a => !string.IsNullOrWhiteSpace(a)))
            {
                db.ClientAllergies.Add(new ClientAllergy
                {
                    ClientId = clientId,
                    Name = allergyName.Trim(),
                    Severity = AllergySeverity.Mild,
                    AllergyType = AllergyType.Food,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await db.SaveChangesAsync();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse allergies JSON for client {ClientId}", clientId);
        }
    }

    private async Task MapMedicationsToClientAsync(AppDbContext db, int clientId, Dictionary<string, string> responses)
    {
        if (!responses.TryGetValue("medical_history.medications", out var medsJson))
            return;

        try
        {
            var medications = JsonSerializer.Deserialize<List<string>>(medsJson);
            if (medications is null) return;

            foreach (var medName in medications.Where(m => !string.IsNullOrWhiteSpace(m)))
            {
                db.ClientMedications.Add(new ClientMedication
                {
                    ClientId = clientId,
                    Name = medName.Trim(),
                    CreatedAt = DateTime.UtcNow
                });
            }

            await db.SaveChangesAsync();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse medications JSON for client {ClientId}", clientId);
        }
    }

    private async Task MapConditionsToClientAsync(AppDbContext db, int clientId, Dictionary<string, string> responses)
    {
        if (!responses.TryGetValue("medical_history.conditions", out var conditionsJson))
            return;

        try
        {
            var conditions = JsonSerializer.Deserialize<List<string>>(conditionsJson);
            if (conditions is null) return;

            foreach (var conditionName in conditions.Where(c => !string.IsNullOrWhiteSpace(c)))
            {
                db.ClientConditions.Add(new ClientCondition
                {
                    ClientId = clientId,
                    Name = conditionName.Trim(),
                    Status = ConditionStatus.Active,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await db.SaveChangesAsync();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse conditions JSON for client {ClientId}", clientId);
        }
    }

    private async Task MapDietaryRestrictionsToClientAsync(AppDbContext db, int clientId, Dictionary<string, string> responses)
    {
        if (!responses.TryGetValue("dietary_habits.dietary_restrictions", out var restrictionsJson))
            return;

        try
        {
            var restrictions = JsonSerializer.Deserialize<List<string>>(restrictionsJson);
            if (restrictions is null) return;

            foreach (var restriction in restrictions.Where(r => !string.IsNullOrWhiteSpace(r)))
            {
                var restrictionType = Enum.TryParse<DietaryRestrictionType>(restriction.Trim().Replace(" ", ""), true, out var parsed)
                    ? parsed
                    : DietaryRestrictionType.Other;

                db.ClientDietaryRestrictions.Add(new ClientDietaryRestriction
                {
                    ClientId = clientId,
                    RestrictionType = restrictionType,
                    Notes = restrictionType == DietaryRestrictionType.Other ? restriction.Trim() : null,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await db.SaveChangesAsync();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse dietary restrictions JSON for client {ClientId}", clientId);
        }
    }

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }

    private async Task TryDispatchAsync(string entityType, int entityId, EntityChangeType changeType, string practitionerUserId)
    {
        try
        {
            await _notificationDispatcher.DispatchAsync(new EntityChangeNotification(
                entityType, entityId, changeType, practitionerUserId, DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispatch {ChangeType} notification for {EntityType} {EntityId}",
                changeType, entityType, entityId);
        }
    }

    private static IntakeFormDto MapToDto(IntakeForm entity)
    {
        return new IntakeFormDto(
            entity.Id,
            entity.ClientId,
            entity.AppointmentId,
            entity.Token,
            entity.Status,
            entity.ClientEmail,
            entity.ExpiresAt,
            entity.SubmittedAt,
            entity.ReviewedAt,
            entity.ReviewedByUserId,
            entity.CreatedByUserId,
            entity.CreatedAt,
            entity.Responses.Select(r => new IntakeFormResponseDto(
                r.SectionKey,
                r.FieldKey,
                r.Value)).ToList());
    }

}
