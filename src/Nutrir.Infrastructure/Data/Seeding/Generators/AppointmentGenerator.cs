using Bogus;
using Nutrir.Core.Entities;
using Nutrir.Core.Enums;

namespace Nutrir.Infrastructure.Data.Seeding.Generators;

public class AppointmentGenerator
{
    private static readonly string[] InitialConsultationNotes =
    [
        "Initial assessment and goal setting",
        "Comprehensive nutrition evaluation and baseline measurements",
        "Reviewed medical history and current dietary habits",
        "Discussed health goals and created preliminary nutrition plan",
        "Initial intake: assessed lifestyle, allergies, and dietary preferences"
    ];

    private static readonly string[] FollowUpNotes =
    [
        "Reviewed food diary and adjusted meal plan",
        "Progress check on nutritional goals; updated macros",
        "Discussed challenges with meal prep; provided new recipes",
        "Adjusted calorie targets based on recent lab results",
        "Reviewed supplement regimen and made modifications",
        "Addressed digestive concerns and updated elimination plan"
    ];

    private static readonly string[] CheckInNotes =
    [
        "Quick check-in on adherence to current plan",
        "Brief progress review; client reports feeling more energetic",
        "Weigh-in and hydration check; on track with goals",
        "Short follow-up on supplement tolerance",
        "Checked in on meal prep routine; no adjustments needed"
    ];

    private static readonly string[] CancellationReasons =
    [
        "Client had a scheduling conflict",
        "Client feeling unwell",
        "Family emergency",
        "Work commitment",
        "Transportation issue"
    ];

    private readonly Faker _faker;

    public AppointmentGenerator(Faker faker)
    {
        _faker = faker;
    }

    public List<Appointment> Generate(List<GeneratedClient> clients, int avgPerClient)
    {
        var appointments = new List<Appointment>();
        // Track occupied slots per nutritionist: NutritionistId -> list of (start, end) in UTC
        var occupiedSlots = new Dictionary<string, List<(DateTime Start, DateTime End)>>();
        var now = DateTime.UtcNow;

        foreach (var generatedClient in clients)
        {
            var client = generatedClient.Client;
            var count = Math.Max(1, avgPerClient + _faker.Random.Int(-2, 2));

            // Split appointments: ~65% future (next 1-21 days), ~35% historical
            var futureTarget = Math.Max(1, (int)Math.Ceiling(count * 0.65));
            var pastTarget = count - futureTarget;

            // Generate candidate slots for each window separately
            var pastCandidates = pastTarget > 0
                ? GenerateCandidateSlots(client.CreatedAt, now, pastTarget * 4)
                : [];
            var futureCandidates = GenerateCandidateSlots(now.AddHours(1), now.AddDays(21), futureTarget * 5);

            if (!occupiedSlots.ContainsKey(client.PrimaryNutritionistId))
            {
                occupiedSlots[client.PrimaryNutritionistId] = new List<(DateTime, DateTime)>();
            }

            var nutritionistSlots = occupiedSlots[client.PrimaryNutritionistId];

            // Generate past appointments first (up to pastTarget)
            var pastGenerated = GenerateFromCandidates(pastCandidates, pastTarget, client, nutritionistSlots, now, appointments);
            // Then future appointments (up to futureTarget, plus any shortfall from past)
            var futureRemaining = count - pastGenerated;
            GenerateFromCandidates(futureCandidates, futureRemaining, client, nutritionistSlots, now, appointments);
        }

        return appointments;
    }

    private int GenerateFromCandidates(
        List<DateTime> candidateSlots,
        int targetCount,
        Client client,
        List<(DateTime Start, DateTime End)> nutritionistSlots,
        DateTime now,
        List<Appointment> appointments)
    {
        var generated = 0;
        var hasInitial = appointments.Any(a => a.ClientId == client.Id && a.Type == AppointmentType.InitialConsultation);

        foreach (var slotStart in candidateSlots)
        {
            if (generated >= targetCount)
                break;

            var type = !hasInitial && generated == 0
                ? AppointmentType.InitialConsultation
                : _faker.Random.WeightedRandom(
                    [AppointmentType.FollowUp, AppointmentType.CheckIn],
                    [0.7f, 0.3f]);

            var duration = type switch
            {
                AppointmentType.InitialConsultation => 60,
                AppointmentType.FollowUp => _faker.Random.Bool() ? 30 : 45,
                AppointmentType.CheckIn => 15,
                _ => 30
            };

            var slotEnd = slotStart.AddMinutes(duration);

            if (HasOverlap(nutritionistSlots, slotStart, slotEnd))
                continue;

            var isFuture = slotStart > now;

            var status = isFuture
                ? _faker.Random.WeightedRandom(
                    [AppointmentStatus.Scheduled, AppointmentStatus.Confirmed],
                    [0.6f, 0.4f])
                : _faker.Random.WeightedRandom(
                    [
                        AppointmentStatus.Completed,
                        AppointmentStatus.NoShow,
                        AppointmentStatus.Cancelled,
                        AppointmentStatus.LateCancellation
                    ],
                    [0.7f, 0.1f, 0.1f, 0.1f]);

            var location = _faker.Random.WeightedRandom(
                [AppointmentLocation.InPerson, AppointmentLocation.Virtual, AppointmentLocation.Phone],
                [0.4f, 0.4f, 0.2f]);

            var createdAt = slotStart.AddDays(-_faker.Random.Int(1, 3));
            if (createdAt < client.CreatedAt)
                createdAt = client.CreatedAt;

            var appointment = new Appointment
            {
                ClientId = client.Id,
                NutritionistId = client.PrimaryNutritionistId,
                Type = type,
                Status = status,
                StartTime = slotStart,
                DurationMinutes = duration,
                Location = location,
                Notes = PickNotes(type),
                CreatedAt = createdAt
            };

            switch (location)
            {
                case AppointmentLocation.Virtual:
                    appointment.VirtualMeetingUrl =
                        $"https://meet.example.com/{client.FirstName.ToLowerInvariant()}";
                    break;
                case AppointmentLocation.InPerson:
                    appointment.LocationNotes = "Office A, Suite 204";
                    break;
            }

            if (status is AppointmentStatus.Cancelled or AppointmentStatus.LateCancellation)
            {
                appointment.CancellationReason = _faker.PickRandom(CancellationReasons);
                appointment.CancelledAt = _faker.Date.Between(createdAt, slotStart).ToUniversalTime();
            }

            if (type == AppointmentType.InitialConsultation)
                hasInitial = true;

            nutritionistSlots.Add((slotStart, slotEnd));
            appointments.Add(appointment);
            generated++;
        }

        return generated;
    }

    private List<DateTime> GenerateCandidateSlots(DateTime windowStart, DateTime windowEnd, int count)
    {
        var slots = new List<DateTime>();
        var totalDays = (int)(windowEnd - windowStart).TotalDays;
        if (totalDays <= 0)
            totalDays = 1;

        for (var i = 0; i < count; i++)
        {
            var dayOffset = _faker.Random.Int(0, totalDays);
            var candidate = windowStart.Date.AddDays(dayOffset);

            // Ensure Monday-Friday
            while (candidate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                candidate = candidate.AddDays(1);
            }

            // Business hours 9am-4pm ET (UTC-5), so 14:00-21:00 UTC
            var hour = _faker.Random.Int(14, 20); // 9am-4pm ET in UTC
            var minute = _faker.PickRandom(0, 15, 30, 45);

            var slot = candidate.AddHours(hour).AddMinutes(minute);

            // Keep within window bounds
            if (slot >= windowStart && slot <= windowEnd)
            {
                slots.Add(DateTime.SpecifyKind(slot, DateTimeKind.Utc));
            }
        }

        slots.Sort();
        return slots;
    }

    private static bool HasOverlap(
        List<(DateTime Start, DateTime End)> existingSlots,
        DateTime newStart,
        DateTime newEnd)
    {
        foreach (var (start, end) in existingSlots)
        {
            if (newStart < end && newEnd > start)
                return true;
        }

        return false;
    }

    private string PickNotes(AppointmentType type)
    {
        return type switch
        {
            AppointmentType.InitialConsultation => _faker.PickRandom(InitialConsultationNotes),
            AppointmentType.FollowUp => _faker.PickRandom(FollowUpNotes),
            AppointmentType.CheckIn => _faker.PickRandom(CheckInNotes),
            _ => _faker.PickRandom(FollowUpNotes)
        };
    }
}
