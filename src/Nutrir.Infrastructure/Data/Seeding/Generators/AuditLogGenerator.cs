using Bogus;
using Nutrir.Core.Entities;
using Nutrir.Core.Enums;

namespace Nutrir.Infrastructure.Data.Seeding.Generators;

public class AuditLogGenerator
{
    private static readonly string[] IpAddresses =
    [
        "192.168.1.10",
        "192.168.1.15",
        "10.0.0.5",
        "172.16.0.100",
        "192.168.1.22"
    ];

    private readonly Faker _faker;

    public AuditLogGenerator(Faker faker)
    {
        _faker = faker;
    }

    public List<AuditLogEntry> Generate(
        List<Client> clients,
        List<Appointment> appointments,
        List<MealPlan> mealPlans,
        string[] nutritionistIds)
    {
        var entries = new List<AuditLogEntry>();

        // Client created entries
        foreach (var client in clients)
        {
            entries.Add(CreateEntry(
                timestamp: client.CreatedAt,
                action: "ClientCreated",
                entityType: "Client",
                entityId: client.Id.ToString(),
                details: $"Created client {client.FirstName} {client.LastName}",
                nutritionistIds));
        }

        // Client updated entries for ~60% of non-deleted clients
        var activeClients = clients.Where(c => !c.IsDeleted).ToList();
        foreach (var client in activeClients)
        {
            if (_faker.Random.Float() > 0.6f)
                continue;

            var updateCount = _faker.Random.Int(1, 3);
            for (var i = 0; i < updateCount; i++)
            {
                var updateTime = _faker.Date.Between(client.CreatedAt, DateTime.UtcNow).ToUniversalTime();
                entries.Add(CreateEntry(
                    timestamp: updateTime,
                    action: "ClientUpdated",
                    entityType: "Client",
                    entityId: client.Id.ToString(),
                    details: $"Updated client {client.FirstName} {client.LastName}",
                    nutritionistIds));
            }
        }

        // Appointment created entries
        foreach (var appointment in appointments)
        {
            entries.Add(CreateEntry(
                timestamp: appointment.CreatedAt > DateTime.UtcNow
                    ? DateTime.UtcNow.AddMinutes(-_faker.Random.Int(5, 120))
                    : appointment.CreatedAt,
                action: "AppointmentCreated",
                entityType: "Appointment",
                entityId: appointment.Id.ToString(),
                details: $"Created appointment for {appointment.StartTime:yyyy-MM-dd}",
                nutritionistIds));
        }

        // Meal plan created entries
        foreach (var mealPlan in mealPlans)
        {
            entries.Add(CreateEntry(
                timestamp: mealPlan.CreatedAt > DateTime.UtcNow
                    ? DateTime.UtcNow.AddMinutes(-_faker.Random.Int(5, 120))
                    : mealPlan.CreatedAt,
                action: "MealPlanCreated",
                entityType: "MealPlan",
                entityId: mealPlan.Id.ToString(),
                details: $"Created meal plan: {mealPlan.Title}",
                nutritionistIds));
        }

        entries.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return entries;
    }

    private AuditLogEntry CreateEntry(
        DateTime timestamp,
        string action,
        string entityType,
        string entityId,
        string details,
        string[] nutritionistIds)
    {
        var source = _faker.Random.Float() < 0.85f
            ? AuditSource.Web
            : AuditSource.AiAssistant;

        return new AuditLogEntry
        {
            Timestamp = timestamp,
            UserId = _faker.PickRandom(nutritionistIds),
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details,
            IpAddress = _faker.PickRandom(IpAddresses),
            Source = source
        };
    }
}
