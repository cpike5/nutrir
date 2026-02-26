using Bogus;
using Nutrir.Core.Entities;
using Nutrir.Core.Enums;

namespace Nutrir.Infrastructure.Data.Seeding.Generators;

public record GeneratedClient(Client Client, ClientProfile Profile, List<ConsentEvent> ConsentEvents);

public class ClientGenerator
{
    private static readonly string[] CanadianAreaCodes =
        ["416", "905", "647", "514", "604", "403", "613", "250", "306", "204", "709", "819"];

    private readonly Faker _faker;

    public ClientGenerator(Faker faker)
    {
        _faker = faker;
    }

    public List<GeneratedClient> Generate(int count, string[] nutritionistIds)
    {
        var nameFaker = new Faker("en_CA");
        var results = new List<GeneratedClient>(count);
        var now = DateTime.UtcNow;

        // Generate CreatedAt dates staggered over 1-90 days ago, then sort oldest first
        var createdDates = Enumerable.Range(0, count)
            .Select(_ => now.AddDays(-_faker.Random.Double(1, 90)))
            .OrderBy(d => d)
            .ToList();

        for (var i = 0; i < count; i++)
        {
            var firstName = nameFaker.Name.FirstName();
            var lastName = nameFaker.Name.LastName();
            var createdAt = createdDates[i];
            var profiles = ClientProfile.All;
            var profile = profiles[_faker.Random.Int(0, profiles.Count - 1)];
            var nutritionistId = _faker.PickRandom(nutritionistIds);
            var dateOfBirth = GenerateDateOfBirth();

            var hasEmail = _faker.Random.Bool(0.9f);
            var areaCode = _faker.PickRandom(CanadianAreaCodes);
            var phone = $"({areaCode}) {_faker.Random.Number(200, 999):D3}-{_faker.Random.Number(0, 9999):D4}";

            // Determine consent and deletion status
            // ~3% withdrawn, ~75% consented (of non-withdrawn), ~7% deleted
            var roll = _faker.Random.Double();
            var isWithdrawn = roll < 0.03;
            var isConsented = !isWithdrawn && roll < 0.78; // 0.03..0.78 = 75%
            var isDeleted = _faker.Random.Bool(0.07f);

            var consentTimestamp = isConsented || isWithdrawn
                ? createdAt.AddMinutes(_faker.Random.Double(0, 60))
                : (DateTime?)null;

            var client = new Client
            {
                FirstName = firstName,
                LastName = lastName,
                Email = hasEmail
                    ? $"{firstName.ToLowerInvariant()}.{lastName.ToLowerInvariant()}@{nameFaker.Internet.DomainName()}"
                    : null,
                Phone = phone,
                DateOfBirth = dateOfBirth,
                PrimaryNutritionistId = nutritionistId,
                ConsentGiven = isConsented,
                ConsentTimestamp = consentTimestamp,
                ConsentPolicyVersion = isConsented || isWithdrawn ? "1.0" : null,
                Notes = _faker.PickRandom(profile.NoteTemplates),
                IsDeleted = isDeleted,
                CreatedAt = createdAt,
                UpdatedAt = isDeleted || isWithdrawn ? createdAt.AddDays(_faker.Random.Double(1, 30)) : null,
                DeletedAt = isDeleted ? createdAt.AddDays(_faker.Random.Double(1, 30)) : null,
                DeletedBy = isDeleted ? nutritionistId : null,
            };

            var consentEvents = new List<ConsentEvent>();

            if (isConsented || isWithdrawn)
            {
                consentEvents.Add(new ConsentEvent
                {
                    EventType = ConsentEventType.ConsentGiven,
                    ConsentPurpose = "Data collection and nutrition services",
                    PolicyVersion = "1.0",
                    Timestamp = consentTimestamp!.Value,
                    RecordedByUserId = nutritionistId,
                });
            }

            if (isWithdrawn)
            {
                var withdrawnAt = consentTimestamp!.Value.AddDays(_faker.Random.Double(7, 60));
                consentEvents.Add(new ConsentEvent
                {
                    EventType = ConsentEventType.ConsentWithdrawn,
                    ConsentPurpose = "Data collection and nutrition services",
                    PolicyVersion = "1.0",
                    Timestamp = withdrawnAt,
                    RecordedByUserId = nutritionistId,
                    Notes = "Client requested withdrawal of consent.",
                });
            }

            results.Add(new GeneratedClient(client, profile, consentEvents));
        }

        return results;
    }

    private DateOnly GenerateDateOfBirth()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var roll = _faker.Random.Double();

        // Age distribution: 18-29 (15%), 30-44 (35%), 45-59 (25%), 60-74 (15%), 75+ (5%), minors 13-17 (5%)
        int minAge, maxAge;
        if (roll < 0.05)
        {
            minAge = 13;
            maxAge = 17;
        }
        else if (roll < 0.20)
        {
            minAge = 18;
            maxAge = 29;
        }
        else if (roll < 0.55)
        {
            minAge = 30;
            maxAge = 44;
        }
        else if (roll < 0.80)
        {
            minAge = 45;
            maxAge = 59;
        }
        else if (roll < 0.95)
        {
            minAge = 60;
            maxAge = 74;
        }
        else
        {
            minAge = 75;
            maxAge = 90;
        }

        var age = _faker.Random.Int(minAge, maxAge);
        var birthYear = today.Year - age;
        var startDate = new DateOnly(birthYear, 1, 1);
        var endDate = new DateOnly(birthYear, 12, 31);

        return _faker.Date.BetweenDateOnly(startDate, endDate);
    }
}
