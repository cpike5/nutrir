using Bogus;
using Nutrir.Core.Entities;
using Nutrir.Core.Enums;
using Nutrir.Infrastructure.Data.Seeding;

namespace Nutrir.Infrastructure.Data.Seeding.Generators;

public class MealPlanGenerator
{
    private static readonly string[] GeneralInstructions =
    [
        "Drink at least 8 glasses of water throughout the day. Hydration supports digestion and energy levels.",
        "Prepare meals in advance when possible. Batch cooking on weekends can simplify weekday adherence.",
        "Eat slowly and mindfully, aiming for 20 minutes per meal. This supports satiety signalling and digestion.",
        "Store prepped ingredients in clear containers in the fridge so healthy options are visible and easy to grab."
    ];

    private static readonly string[] WeekdayNames =
        ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

    private readonly Faker _faker;

    public MealPlanGenerator(Faker faker)
    {
        _faker = faker;
    }

    public List<MealPlan> Generate(List<GeneratedClient> clients, int avgPerClient, string[] nutritionistIds)
    {
        var plans = new List<MealPlan>();

        var eligibleClients = clients
            .Where(gc => gc.Client.ConsentGiven && !gc.Client.IsDeleted)
            .ToList();

        foreach (var gc in eligibleClients)
        {
            var count = _faker.Random.Int(
                Math.Max(0, avgPerClient - 1),
                avgPerClient + 1);

            for (var i = 0; i < count; i++)
            {
                var plan = GeneratePlan(gc, nutritionistIds);
                plans.Add(plan);
            }
        }

        return plans;
    }

    private MealPlan GeneratePlan(GeneratedClient gc, string[] nutritionistIds)
    {
        var profile = gc.Profile;
        var status = PickStatus();
        var template = _faker.PickRandom(profile.MealPlanTemplates);
        var macros = profile.MacroTargets;

        var startDate = PickStartDate(status);
        var dayCount = _faker.Random.Int(3, 7);
        var endDate = startDate.HasValue ? startDate.Value.AddDays(dayCount - 1) : (DateOnly?)null;
        var createdAt = PickCreatedAt(status, startDate);

        var plan = new MealPlan
        {
            ClientId = gc.Client.Id,
            CreatedByUserId = _faker.PickRandom(nutritionistIds),
            Title = template.Title,
            Description = template.Description,
            Status = status,
            StartDate = startDate,
            EndDate = endDate,
            CalorieTarget = _faker.Random.Int(macros.MinCalories, macros.MaxCalories),
            ProteinTargetG = _faker.Random.Int(macros.MinProtein, macros.MaxProtein),
            CarbsTargetG = _faker.Random.Int(macros.MinCarbs, macros.MaxCarbs),
            FatTargetG = _faker.Random.Int(macros.MinFat, macros.MaxFat),
            Notes = _faker.PickRandom(profile.NoteTemplates),
            Instructions = _faker.PickRandom(GeneralInstructions),
            IsDeleted = false,
            CreatedAt = createdAt
        };

        var foodPool = FoodDatabase.GetByTags(profile.FoodPoolTags);

        for (var d = 0; d < dayCount; d++)
        {
            var day = new MealPlanDay
            {
                MealPlanId = plan.Id,
                DayNumber = d + 1,
                Label = dayCount == 7 ? WeekdayNames[d] : $"Day {d + 1}",
                Notes = _faker.Random.Bool(0.3f) ? _faker.PickRandom(profile.NoteTemplates) : null
            };

            var slots = GenerateSlots(foodPool);
            day.MealSlots = slots;
            plan.Days.Add(day);
        }

        return plan;
    }

    private MealPlanStatus PickStatus()
    {
        var roll = _faker.Random.Int(1, 100);
        return roll switch
        {
            <= 50 => MealPlanStatus.Active,
            <= 70 => MealPlanStatus.Draft,
            _ => MealPlanStatus.Archived
        };
    }

    private DateOnly? PickStartDate(MealPlanStatus status)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        return status switch
        {
            MealPlanStatus.Active => today.AddDays(-_faker.Random.Int(0, 3)),
            MealPlanStatus.Archived => today.AddDays(-_faker.Random.Int(30, 120)),
            MealPlanStatus.Draft => _faker.Random.Bool(0.5f)
                ? today.AddDays(_faker.Random.Int(1, 14))
                : null,
            _ => null
        };
    }

    private DateTime PickCreatedAt(MealPlanStatus status, DateOnly? startDate)
    {
        if (startDate.HasValue)
        {
            var daysBeforeStart = _faker.Random.Int(1, 5);
            return startDate.Value.AddDays(-daysBeforeStart)
                .ToDateTime(new TimeOnly(_faker.Random.Int(8, 18), _faker.Random.Int(0, 59)), DateTimeKind.Utc);
        }

        // Drafts without a start date: created recently
        var daysAgo = _faker.Random.Int(0, 14);
        return DateTime.UtcNow.AddDays(-daysAgo)
            .Date
            .AddHours(_faker.Random.Int(8, 18))
            .AddMinutes(_faker.Random.Int(0, 59));
    }

    private List<MealSlot> GenerateSlots(IReadOnlyList<FoodEntry> foodPool)
    {
        var slots = new List<MealSlot>();
        var sortOrder = 0;

        // Always include Breakfast, Lunch, Dinner; optionally MorningSnack and AfternoonSnack
        var includeMorningSnack = _faker.Random.Bool(0.5f);
        var includeAfternoonSnack = _faker.Random.Bool(0.5f);

        AddSlot(slots, MealType.Breakfast, ref sortOrder, foodPool);

        if (includeMorningSnack)
            AddSlot(slots, MealType.MorningSnack, ref sortOrder, foodPool);

        AddSlot(slots, MealType.Lunch, ref sortOrder, foodPool);

        if (includeAfternoonSnack)
            AddSlot(slots, MealType.AfternoonSnack, ref sortOrder, foodPool);

        AddSlot(slots, MealType.Dinner, ref sortOrder, foodPool);

        return slots;
    }

    private void AddSlot(List<MealSlot> slots, MealType mealType, ref int sortOrder, IReadOnlyList<FoodEntry> foodPool)
    {
        var slot = new MealSlot
        {
            MealType = mealType,
            SortOrder = sortOrder++,
            Notes = _faker.Random.Bool(0.2f) ? _faker.Lorem.Sentence() : null
        };

        var itemCount = mealType is MealType.MorningSnack or MealType.AfternoonSnack
            ? _faker.Random.Int(1, 2)
            : _faker.Random.Int(1, 3);

        var selectedFoods = _faker.PickRandom(foodPool, Math.Min(itemCount, foodPool.Count)).ToList();

        for (var i = 0; i < selectedFoods.Count; i++)
        {
            var food = selectedFoods[i];
            slot.Items.Add(new MealItem
            {
                FoodName = food.Name,
                Quantity = food.Quantity,
                Unit = food.Unit,
                CaloriesKcal = food.CaloriesKcal,
                ProteinG = food.ProteinG,
                CarbsG = food.CarbsG,
                FatG = food.FatG,
                Notes = food.Notes,
                SortOrder = i
            });
        }

        slots.Add(slot);
    }
}
