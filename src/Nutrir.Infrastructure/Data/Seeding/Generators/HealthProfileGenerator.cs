using Bogus;
using Nutrir.Core.Entities;

namespace Nutrir.Infrastructure.Data.Seeding.Generators;

public record GeneratedHealthProfile(
    List<ClientAllergy> Allergies,
    List<ClientMedication> Medications,
    List<ClientCondition> Conditions,
    List<ClientDietaryRestriction> DietaryRestrictions);

public class HealthProfileGenerator
{
    private readonly Faker _faker;

    public HealthProfileGenerator(Faker faker)
    {
        _faker = faker;
    }

    public GeneratedHealthProfile Generate(List<GeneratedClient> clients)
    {
        var allergies = new List<ClientAllergy>();
        var medications = new List<ClientMedication>();
        var conditions = new List<ClientCondition>();
        var dietaryRestrictions = new List<ClientDietaryRestriction>();

        var eligibleClients = clients
            .Where(c => c.Client.ConsentGiven && !c.Client.IsDeleted)
            .ToList();

        foreach (var generated in eligibleClients)
        {
            // ~82% of clients get health profile data
            if (!_faker.Random.Bool(0.82f))
                continue;

            var client = generated.Client;
            var profile = generated.Profile;
            var baseDate = client.CreatedAt.AddDays(_faker.Random.Double(0, 2));

            // Allergies: 0-3 from pool (weighted: 15% get 0, 40% get 1, 30% get 2, 15% get 3)
            if (profile.AllergyPool.Length > 0)
            {
                var allergyCount = PickWeightedCount(profile.AllergyPool.Length, 3, [0.15, 0.40, 0.30, 0.15]);
                var selectedAllergies = _faker.PickRandom(profile.AllergyPool, allergyCount).Distinct().ToList();
                foreach (var a in selectedAllergies)
                {
                    allergies.Add(new ClientAllergy
                    {
                        ClientId = client.Id,
                        Name = a.Name,
                        Severity = a.Severity,
                        AllergyType = a.Type,
                        CreatedAt = baseDate,
                    });
                }
            }

            // Medications: 1-3 from pool
            if (profile.MedicationPool.Length > 0)
            {
                var medCount = _faker.Random.Int(1, Math.Min(3, profile.MedicationPool.Length));
                var selectedMeds = _faker.PickRandom(profile.MedicationPool, medCount).Distinct().ToList();
                foreach (var m in selectedMeds)
                {
                    medications.Add(new ClientMedication
                    {
                        ClientId = client.Id,
                        Name = m.Name,
                        Dosage = m.Dosage,
                        Frequency = m.Frequency,
                        PrescribedFor = m.PrescribedFor,
                        CreatedAt = baseDate,
                    });
                }
            }

            // Conditions: 1-2 from pool
            if (profile.ConditionPool.Length > 0)
            {
                var condCount = _faker.Random.Int(1, Math.Min(2, profile.ConditionPool.Length));
                var selectedConditions = _faker.PickRandom(profile.ConditionPool, condCount).Distinct().ToList();
                foreach (var c in selectedConditions)
                {
                    conditions.Add(new ClientCondition
                    {
                        ClientId = client.Id,
                        Name = c.Name,
                        Code = c.Code,
                        Status = c.Status,
                        Notes = c.Notes,
                        DiagnosisDate = _faker.Random.Bool(0.6f)
                            ? DateOnly.FromDateTime(client.CreatedAt.AddDays(-_faker.Random.Int(30, 730)))
                            : null,
                        CreatedAt = baseDate,
                    });
                }
            }

            // Restrictions: 0-2 from pool
            if (profile.RestrictionPool.Length > 0)
            {
                var restCount = _faker.Random.Int(0, Math.Min(2, profile.RestrictionPool.Length));
                var selectedRestrictions = _faker.PickRandom(profile.RestrictionPool, restCount).Distinct().ToList();
                foreach (var r in selectedRestrictions)
                {
                    dietaryRestrictions.Add(new ClientDietaryRestriction
                    {
                        ClientId = client.Id,
                        RestrictionType = r,
                        CreatedAt = baseDate,
                    });
                }
            }
        }

        return new GeneratedHealthProfile(allergies, medications, conditions, dietaryRestrictions);
    }

    private int PickWeightedCount(int poolSize, int maxCount, double[] weights)
    {
        var effectiveMax = Math.Min(maxCount, poolSize);
        var roll = _faker.Random.Double();
        var cumulative = 0.0;
        for (var i = 0; i <= effectiveMax; i++)
        {
            cumulative += i < weights.Length ? weights[i] : 0;
            if (roll < cumulative)
                return i;
        }
        return effectiveMax;
    }
}
