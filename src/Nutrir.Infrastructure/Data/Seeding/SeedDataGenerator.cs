using Bogus;
using Nutrir.Core.Entities;
using Nutrir.Infrastructure.Data.Seeding.Generators;

namespace Nutrir.Infrastructure.Data.Seeding;

/// <summary>
/// Orchestrates seed data generation in stages to allow FK-dependent persistence.
/// Must be called in order: GenerateClients → (save) → GenerateChildEntities → (save) → GenerateAuditLogs
/// </summary>
public class SeedDataGenerator
{
    private readonly SeedOptions _options;
    private readonly Faker _faker;

    // Retained across stages for cross-generator access
    private List<GeneratedClient>? _generatedClients;
    private List<Appointment>? _appointments;
    private List<MealPlan>? _mealPlans;

    public SeedDataGenerator(SeedOptions options)
    {
        _options = options;
        var seed = options.RandomSeed ?? Randomizer.Seed.Next();
        Randomizer.Seed = new Random(seed);
        _faker = new Faker("en_CA");
    }

    /// <summary>
    /// Stage 1: Generate clients and consent events. Save these before calling Stage 2.
    /// </summary>
    public List<GeneratedClient> GenerateClients(string[] nutritionistIds)
    {
        var clientGenerator = new ClientGenerator(_faker);
        _generatedClients = clientGenerator.Generate(_options.ClientCount, nutritionistIds);
        return _generatedClients;
    }

    /// <summary>
    /// Stage 2: Generate appointments, meal plans, progress. Clients must already be persisted (have real IDs).
    /// </summary>
    public (List<Appointment> Appointments, List<MealPlan> MealPlans, List<ProgressGoal> Goals, List<ProgressEntry> Entries)
        GenerateChildEntities(string[] nutritionistIds)
    {
        if (_generatedClients is null)
            throw new InvalidOperationException("Call GenerateClients first.");

        var appointmentGenerator = new AppointmentGenerator(_faker);
        _appointments = appointmentGenerator.Generate(_generatedClients, _options.AppointmentsPerClient);

        var mealPlanGenerator = new MealPlanGenerator(_faker);
        _mealPlans = mealPlanGenerator.Generate(_generatedClients, _options.MealPlansPerClient, nutritionistIds);

        var progressGenerator = new ProgressGenerator(_faker);
        var progress = progressGenerator.Generate(_generatedClients, _options.ProgressEntriesPerClient, nutritionistIds);

        return (_appointments, _mealPlans, progress.Goals, progress.Entries);
    }

    /// <summary>
    /// Stage 3: Generate audit logs derived from persisted entities.
    /// </summary>
    public List<AuditLogEntry> GenerateAuditLogs(string[] nutritionistIds)
    {
        if (_generatedClients is null || _appointments is null || _mealPlans is null)
            throw new InvalidOperationException("Call GenerateClients and GenerateChildEntities first.");

        var clients = _generatedClients.Select(gc => gc.Client).ToList();
        var auditLogGenerator = new AuditLogGenerator(_faker);
        return auditLogGenerator.Generate(clients, _appointments, _mealPlans, nutritionistIds);
    }
}
