using Bogus;
using Nutrir.Core.Entities;
using Nutrir.Core.Enums;
using Nutrir.Infrastructure.Data.Seeding;

namespace Nutrir.Infrastructure.Data.Seeding.Generators;

public record GeneratedProgress(List<ProgressGoal> Goals, List<ProgressEntry> Entries);

public class ProgressGenerator
{
    private static readonly string[] ProgressNoteTemplates =
    [
        "Feeling good this week, energy levels are up.",
        "Struggling with adherence, especially on weekends.",
        "Noticed visible changes in how clothes fit.",
        "Had a challenging week but stayed on track overall.",
        "Feeling stronger and more confident with the plan.",
        "Missed a couple of meals this week due to schedule conflicts."
    ];

    private readonly Faker _faker;

    public ProgressGenerator(Faker faker)
    {
        _faker = faker;
    }

    public GeneratedProgress Generate(List<GeneratedClient> clients, int avgEntriesPerClient, string[] nutritionistIds)
    {
        var goals = new List<ProgressGoal>();
        var entries = new List<ProgressEntry>();

        var goalId = 1;
        var entryId = 1;
        var measurementId = 1;

        var eligibleClients = clients
            .Where(c => c.Client.ConsentGiven && !c.Client.IsDeleted)
            .ToList();

        foreach (var generated in eligibleClients)
        {
            var client = generated.Client;
            var profile = generated.Profile;
            var nutritionistId = _faker.PickRandom(nutritionistIds);

            // Generate 1-3 goals from profile's RelevantGoalTypes
            var goalCount = _faker.Random.Int(1, Math.Min(3, profile.RelevantGoalTypes.Length));
            var selectedGoalTypes = _faker.PickRandom(profile.RelevantGoalTypes, goalCount).ToList();

            foreach (var goalType in selectedGoalTypes)
            {
                var goal = new ProgressGoal
                {
                    Id = goalId++,
                    ClientId = client.Id,
                    CreatedByUserId = nutritionistId,
                    Title = _faker.PickRandom(profile.GoalTitleTemplates),
                    Description = _faker.Random.Bool(0.6f)
                        ? _faker.Lorem.Sentence(8, 6)
                        : null,
                    GoalType = goalType,
                    Status = PickGoalStatus(),
                    IsDeleted = false,
                    CreatedAt = client.CreatedAt.AddDays(_faker.Random.Int(0, 3))
                };

                ApplyGoalTargets(goal, goalType);
                goals.Add(goal);
            }

            // Generate entries distributed across client tenure
            var entryCount = Math.Max(1, avgEntriesPerClient + _faker.Random.Int(-3, 3));
            var clientEntries = GenerateEntries(
                client, profile, nutritionistId,
                entryCount, ref entryId, ref measurementId);
            entries.AddRange(clientEntries);
        }

        return new GeneratedProgress(goals, entries);
    }

    private GoalStatus PickGoalStatus()
    {
        var roll = _faker.Random.Double();
        return roll switch
        {
            < 0.6 => GoalStatus.Active,
            < 0.8 => GoalStatus.Achieved,
            _ => GoalStatus.Abandoned
        };
    }

    private void ApplyGoalTargets(ProgressGoal goal, GoalType goalType)
    {
        switch (goalType)
        {
            case GoalType.Weight:
                goal.TargetValue = _faker.Random.Decimal(60m, 85m);
                goal.TargetUnit = "kg";
                goal.TargetDate = DateOnly.FromDateTime(
                    DateTime.UtcNow.AddMonths(_faker.Random.Int(2, 6)));
                break;

            case GoalType.BodyComposition:
                goal.TargetValue = _faker.Random.Decimal(12m, 22m);
                goal.TargetUnit = "%";
                goal.TargetDate = DateOnly.FromDateTime(
                    DateTime.UtcNow.AddMonths(_faker.Random.Int(2, 6)));
                break;

            case GoalType.Dietary:
            case GoalType.Custom:
            default:
                goal.TargetValue = null;
                goal.TargetUnit = null;
                goal.TargetDate = _faker.Random.Bool(0.5f)
                    ? DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(_faker.Random.Int(2, 6)))
                    : null;
                break;
        }
    }

    private List<ProgressEntry> GenerateEntries(
        Client client,
        ClientProfile profile,
        string nutritionistId,
        int entryCount,
        ref int entryId,
        ref int measurementId)
    {
        var result = new List<ProgressEntry>();
        var now = DateTime.UtcNow;
        var tenureDays = (int)(now - client.CreatedAt).TotalDays;

        if (tenureDays < 1)
            tenureDays = 7;

        // Distribute entries roughly weekly across client tenure
        var intervalDays = Math.Max(1, tenureDays / entryCount);

        // Determine weight trend based on profile tag
        var weightTrendPerWeek = profile.Tag switch
        {
            "weight-management" => _faker.Random.Decimal(-0.5m, -0.2m),
            "sports-nutrition" => _faker.Random.Decimal(0.05m, 0.15m),
            _ => 0m
        };

        var startingWeight = _faker.Random.Decimal(65m, 95m);
        var startingBodyFat = _faker.Random.Decimal(15m, 30m);

        for (var i = 0; i < entryCount; i++)
        {
            var daysOffset = intervalDays * i + _faker.Random.Int(0, Math.Max(0, intervalDays - 1));
            if (daysOffset > tenureDays)
                daysOffset = tenureDays;

            var entryDateTime = client.CreatedAt.AddDays(daysOffset);
            var entryDate = DateOnly.FromDateTime(entryDateTime);
            var weeksFromStart = (decimal)daysOffset / 7m;

            var entry = new ProgressEntry
            {
                Id = entryId++,
                ClientId = client.Id,
                CreatedByUserId = nutritionistId,
                EntryDate = entryDate,
                Notes = _faker.Random.Bool(0.7f)
                    ? _faker.PickRandom(ProgressNoteTemplates)
                    : null,
                IsDeleted = false,
                CreatedAt = entryDateTime
            };

            // Generate 1-3 measurements from profile's RelevantMetrics
            var measurementCount = _faker.Random.Int(1, Math.Min(3, profile.RelevantMetrics.Length));
            var selectedMetrics = _faker.PickRandom(profile.RelevantMetrics, measurementCount).ToList();

            foreach (var metricType in selectedMetrics)
            {
                var measurement = new ProgressMeasurement
                {
                    Id = measurementId++,
                    ProgressEntryId = entry.Id,
                    MetricType = metricType
                };

                ApplyMeasurementValue(measurement, metricType, startingWeight, startingBodyFat, weightTrendPerWeek, weeksFromStart);
                entry.Measurements.Add(measurement);
            }

            result.Add(entry);
        }

        return result;
    }

    private void ApplyMeasurementValue(
        ProgressMeasurement measurement,
        MetricType metricType,
        decimal startingWeight,
        decimal startingBodyFat,
        decimal weightTrendPerWeek,
        decimal weeksFromStart)
    {
        switch (metricType)
        {
            case MetricType.Weight:
                var weightNoise = _faker.Random.Decimal(-0.3m, 0.3m);
                measurement.Value = Math.Round(
                    startingWeight + (weightTrendPerWeek * weeksFromStart) + weightNoise, 1);
                measurement.Unit = "kg";
                break;

            case MetricType.BodyFatPercentage:
                var bfNoise = _faker.Random.Decimal(-0.2m, 0.2m);
                var bfTrend = -0.1m * weeksFromStart;
                measurement.Value = Math.Round(
                    Math.Clamp(startingBodyFat + bfTrend + bfNoise, 8m, 35m), 1);
                measurement.Unit = "%";
                break;

            case MetricType.BloodPressureSystolic:
                measurement.Value = _faker.Random.Decimal(120m, 145m);
                measurement.Unit = "mmHg";
                break;

            case MetricType.BloodPressureDiastolic:
                measurement.Value = _faker.Random.Decimal(75m, 95m);
                measurement.Unit = "mmHg";
                break;

            case MetricType.RestingHeartRate:
                measurement.Value = _faker.Random.Decimal(60m, 85m);
                measurement.Unit = "bpm";
                break;

            case MetricType.WaistCircumference:
                measurement.Value = _faker.Random.Decimal(75m, 105m);
                measurement.Unit = "cm";
                break;

            case MetricType.HipCircumference:
                measurement.Value = _faker.Random.Decimal(85m, 115m);
                measurement.Unit = "cm";
                break;

            case MetricType.BMI:
                measurement.Value = _faker.Random.Decimal(18.5m, 32m);
                measurement.Unit = "kg/m2";
                break;

            case MetricType.Custom:
                measurement.CustomMetricName = "Custom metric";
                measurement.Value = _faker.Random.Decimal(0m, 100m);
                measurement.Unit = null;
                break;

            default:
                measurement.Value = _faker.Random.Decimal(0m, 100m);
                measurement.Unit = null;
                break;
        }
    }
}
