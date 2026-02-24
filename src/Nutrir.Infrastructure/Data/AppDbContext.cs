using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Nutrir.Core.Entities;
using Nutrir.Core.Enums;

namespace Nutrir.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    public DbSet<InviteCode> InviteCodes => Set<InviteCode>();

    public DbSet<Client> Clients => Set<Client>();

    public DbSet<Appointment> Appointments => Set<Appointment>();

    public DbSet<MealPlan> MealPlans => Set<MealPlan>();

    public DbSet<MealPlanDay> MealPlanDays => Set<MealPlanDay>();

    public DbSet<MealSlot> MealSlots => Set<MealSlot>();

    public DbSet<MealItem> MealItems => Set<MealItem>();

    public DbSet<ProgressGoal> ProgressGoals => Set<ProgressGoal>();

    public DbSet<ProgressEntry> ProgressEntries => Set<ProgressEntry>();

    public DbSet<ProgressMeasurement> ProgressMeasurements => Set<ProgressMeasurement>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(u => u.FirstName).HasMaxLength(100);
            entity.Property(u => u.LastName).HasMaxLength(100);
            entity.Property(u => u.DisplayName).HasMaxLength(200);
            entity.Property(u => u.IsActive).HasDefaultValue(true);
            entity.Property(u => u.CreatedDate).HasDefaultValueSql("now() at time zone 'utc'");
        });

        builder.Entity<InviteCode>(entity =>
        {
            entity.HasIndex(ic => ic.Code).IsUnique();
            entity.Property(ic => ic.Code).HasMaxLength(10).IsRequired();
            entity.Property(ic => ic.TargetRole).HasMaxLength(50).IsRequired();
            entity.Property(ic => ic.IsUsed).HasDefaultValue(false);
            entity.Property(ic => ic.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");

            entity.HasOne(ic => ic.CreatedBy)
                .WithMany()
                .HasForeignKey(ic => ic.CreatedById)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(ic => ic.RedeemedBy)
                .WithMany()
                .HasForeignKey(ic => ic.RedeemedById)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Client>(entity =>
        {
            entity.HasQueryFilter(c => !c.IsDeleted);

            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(c => c.PrimaryNutritionistId);

            entity.Property(c => c.FirstName).HasMaxLength(100);
            entity.Property(c => c.LastName).HasMaxLength(100);
            entity.Property(c => c.Email).HasMaxLength(256);
            entity.Property(c => c.Phone).HasMaxLength(20);
            entity.Property(c => c.ConsentPolicyVersion).HasMaxLength(50);
            entity.Property(c => c.Notes).HasColumnType("text");
            entity.Property(c => c.IsDeleted).HasDefaultValue(false);
            entity.Property(c => c.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");
        });

        builder.Entity<Appointment>(entity =>
        {
            entity.HasQueryFilter(a => !a.IsDeleted);

            entity.HasOne<Client>()
                .WithMany()
                .HasForeignKey(a => a.ClientId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(a => a.NutritionistId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(a => a.Type).HasConversion<string>();
            entity.Property(a => a.Status).HasConversion<string>();
            entity.Property(a => a.Location).HasConversion<string>();

            entity.Property(a => a.VirtualMeetingUrl).HasMaxLength(500);
            entity.Property(a => a.LocationNotes).HasMaxLength(500);
            entity.Property(a => a.CancellationReason).HasMaxLength(500);
            entity.Property(a => a.Notes).HasColumnType("text");

            entity.Property(a => a.IsDeleted).HasDefaultValue(false);
            entity.Property(a => a.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");

            entity.Ignore(a => a.EndTime);
        });

        builder.Entity<MealPlan>(entity =>
        {
            entity.HasQueryFilter(mp => !mp.IsDeleted);

            entity.HasOne<Client>()
                .WithMany()
                .HasForeignKey(mp => mp.ClientId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(mp => mp.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(mp => mp.Status).HasConversion<string>();
            entity.Property(mp => mp.Title).HasMaxLength(200);
            entity.Property(mp => mp.Description).HasColumnType("text");
            entity.Property(mp => mp.Notes).HasColumnType("text");
            entity.Property(mp => mp.Instructions).HasColumnType("text");
            entity.Property(mp => mp.CalorieTarget).HasPrecision(18, 2);
            entity.Property(mp => mp.ProteinTargetG).HasPrecision(18, 2);
            entity.Property(mp => mp.CarbsTargetG).HasPrecision(18, 2);
            entity.Property(mp => mp.FatTargetG).HasPrecision(18, 2);
            entity.Property(mp => mp.IsDeleted).HasDefaultValue(false);
            entity.Property(mp => mp.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");

            entity.HasMany(mp => mp.Days)
                .WithOne()
                .HasForeignKey(d => d.MealPlanId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<MealPlanDay>(entity =>
        {
            entity.HasIndex(d => new { d.MealPlanId, d.DayNumber }).IsUnique();
            entity.Property(d => d.Label).HasMaxLength(100);
            entity.Property(d => d.Notes).HasColumnType("text");

            entity.HasMany(d => d.MealSlots)
                .WithOne()
                .HasForeignKey(s => s.MealPlanDayId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<MealSlot>(entity =>
        {
            entity.Property(s => s.MealType).HasConversion<string>();
            entity.Property(s => s.CustomName).HasMaxLength(100);
            entity.Property(s => s.Notes).HasColumnType("text");

            entity.HasMany(s => s.Items)
                .WithOne()
                .HasForeignKey(i => i.MealSlotId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<MealItem>(entity =>
        {
            entity.Property(i => i.FoodName).HasMaxLength(200);
            entity.Property(i => i.Unit).HasMaxLength(50);
            entity.Property(i => i.Quantity).HasPrecision(18, 2);
            entity.Property(i => i.CaloriesKcal).HasPrecision(18, 2);
            entity.Property(i => i.ProteinG).HasPrecision(18, 2);
            entity.Property(i => i.CarbsG).HasPrecision(18, 2);
            entity.Property(i => i.FatG).HasPrecision(18, 2);
            entity.Property(i => i.Notes).HasColumnType("text");
        });

        builder.Entity<ProgressGoal>(entity =>
        {
            entity.HasQueryFilter(g => !g.IsDeleted);

            entity.HasOne<Client>()
                .WithMany()
                .HasForeignKey(g => g.ClientId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(g => g.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(g => g.GoalType).HasConversion<string>();
            entity.Property(g => g.Status).HasConversion<string>();
            entity.Property(g => g.Title).HasMaxLength(200);
            entity.Property(g => g.Description).HasColumnType("text");
            entity.Property(g => g.TargetValue).HasPrecision(18, 4);
            entity.Property(g => g.TargetUnit).HasMaxLength(50);
            entity.Property(g => g.IsDeleted).HasDefaultValue(false);
            entity.Property(g => g.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");
        });

        builder.Entity<ProgressEntry>(entity =>
        {
            entity.HasQueryFilter(e => !e.IsDeleted);

            entity.HasOne<Client>()
                .WithMany()
                .HasForeignKey(e => e.ClientId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(e => e.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(e => e.Notes).HasColumnType("text");
            entity.Property(e => e.IsDeleted).HasDefaultValue(false);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");

            entity.HasMany(e => e.Measurements)
                .WithOne()
                .HasForeignKey(m => m.ProgressEntryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ProgressMeasurement>(entity =>
        {
            entity.Property(m => m.MetricType).HasConversion<string>();
            entity.Property(m => m.CustomMetricName).HasMaxLength(100);
            entity.Property(m => m.Value).HasPrecision(18, 4);
            entity.Property(m => m.Unit).HasMaxLength(50);
        });
    }
}
