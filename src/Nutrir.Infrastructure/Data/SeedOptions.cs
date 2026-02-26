namespace Nutrir.Infrastructure.Data;

public class SeedOptions
{
    public const string SectionName = "Seed";

    public string AdminEmail { get; set; } = "admin@nutrir.ca";

    public string AdminPassword { get; set; } = "ChangeMe123!";

    // Dynamic seed data generation options (dev only)
    public int ClientCount { get; set; } = 20;
    public int AppointmentsPerClient { get; set; } = 4;
    public int MealPlansPerClient { get; set; } = 1;
    public int ProgressEntriesPerClient { get; set; } = 6;
    public int? RandomSeed { get; set; } = 42;
}
