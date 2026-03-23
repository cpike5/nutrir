using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Nutrir.Core.Entities;
using Nutrir.Core.Enums;
using Nutrir.Infrastructure.Security;

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

    public DbSet<ConsentEvent> ConsentEvents => Set<ConsentEvent>();

    public DbSet<ConsentForm> ConsentForms => Set<ConsentForm>();

    public DbSet<AiConversation> AiConversations => Set<AiConversation>();

    public DbSet<AiConversationMessage> AiConversationMessages => Set<AiConversationMessage>();

    public DbSet<AiUsageLog> AiUsageLogs => Set<AiUsageLog>();

    public DbSet<ClientAllergy> ClientAllergies => Set<ClientAllergy>();

    public DbSet<ClientMedication> ClientMedications => Set<ClientMedication>();

    public DbSet<ClientCondition> ClientConditions => Set<ClientCondition>();

    public DbSet<ClientDietaryRestriction> ClientDietaryRestrictions => Set<ClientDietaryRestriction>();

    public DbSet<Condition> Conditions => Set<Condition>();

    public DbSet<PractitionerSchedule> PractitionerSchedules => Set<PractitionerSchedule>();

    public DbSet<PractitionerTimeBlock> PractitionerTimeBlocks => Set<PractitionerTimeBlock>();

    public DbSet<Allergen> Allergens => Set<Allergen>();

    public DbSet<AllergenWarningOverride> AllergenWarningOverrides => Set<AllergenWarningOverride>();

    public DbSet<Medication> Medications => Set<Medication>();

    public DbSet<IntakeForm> IntakeForms => Set<IntakeForm>();

    public DbSet<IntakeFormResponse> IntakeFormResponses => Set<IntakeFormResponse>();

    public DbSet<AppointmentReminder> AppointmentReminders => Set<AppointmentReminder>();

    public DbSet<SessionNote> SessionNotes => Set<SessionNote>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Apply field-level encryption to sensitive Notes fields
        var encryptor = AesGcmFieldEncryptor.Instance;
        EncryptedStringConverter? converter = encryptor is not null
            ? new EncryptedStringConverter(encryptor)
            : null;

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(u => u.FirstName).HasMaxLength(100);
            entity.Property(u => u.LastName).HasMaxLength(100);
            entity.Property(u => u.DisplayName).HasMaxLength(200);
            entity.Property(u => u.IsActive).HasDefaultValue(true);
            entity.Property(u => u.CreatedDate).HasDefaultValueSql("now() at time zone 'utc'");
            entity.Property(u => u.BufferTimeMinutes).HasDefaultValue(15);
        });

        builder.Entity<InviteCode>(entity =>
        {
            entity.HasIndex(ic => ic.Code).IsUnique();
            entity.Property(ic => ic.Code).HasMaxLength(10).IsRequired();
            entity.Property(ic => ic.TargetRole).HasMaxLength(50).IsRequired();
            entity.Property(ic => ic.IsUsed).HasDefaultValue(false);
            entity.Property(ic => ic.IsCancelled).HasDefaultValue(false);
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
            if (converter is not null) entity.Property(c => c.Notes).HasConversion(converter);
            entity.Property(c => c.EmailRemindersEnabled).HasDefaultValue(false);
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
            entity.Property(a => a.PrepNotes).HasColumnType("text");
            if (converter is not null)
            {
                entity.Property(a => a.Notes).HasConversion(converter);
                entity.Property(a => a.PrepNotes).HasConversion(converter);
            }

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
            if (converter is not null)
            {
                entity.Property(mp => mp.Description).HasConversion(converter);
                entity.Property(mp => mp.Notes).HasConversion(converter);
            }
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
            if (converter is not null) entity.Property(d => d.Notes).HasConversion(converter);

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
            if (converter is not null) entity.Property(s => s.Notes).HasConversion(converter);

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
            if (converter is not null) entity.Property(i => i.Notes).HasConversion(converter);
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
            if (converter is not null) entity.Property(g => g.Description).HasConversion(converter);
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
            if (converter is not null) entity.Property(e => e.Notes).HasConversion(converter);
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

        builder.Entity<ConsentEvent>(entity =>
        {
            // No soft-delete, no query filter — append-only
            entity.HasOne<Client>()
                .WithMany(c => c.ConsentEvents)
                .HasForeignKey(ce => ce.ClientId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(ce => ce.EventType).HasConversion<string>();
            entity.Property(ce => ce.ConsentPurpose).HasMaxLength(200);
            entity.Property(ce => ce.PolicyVersion).HasMaxLength(50);
            entity.Property(ce => ce.RecordedByUserId).HasMaxLength(450);
            entity.Property(ce => ce.Notes).HasColumnType("text");
            if (converter is not null) entity.Property(ce => ce.Notes).HasConversion(converter);
            entity.Property(ce => ce.Timestamp).HasDefaultValueSql("now() at time zone 'utc'");
        });

        builder.Entity<ConsentForm>(entity =>
        {
            // No soft-delete — consent forms are append-only records
            entity.HasOne<Client>()
                .WithMany()
                .HasForeignKey(cf => cf.ClientId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(cf => cf.SignatureMethod).HasConversion<string>();
            entity.Property(cf => cf.FormVersion).HasMaxLength(50);
            entity.Property(cf => cf.GeneratedByUserId).HasMaxLength(450);
            entity.Property(cf => cf.SignedByUserId).HasMaxLength(450);
            entity.Property(cf => cf.ScannedCopyPath).HasMaxLength(500);
            entity.Property(cf => cf.Notes).HasColumnType("text");
            if (converter is not null) entity.Property(cf => cf.Notes).HasConversion(converter);
            entity.Property(cf => cf.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");
        });

        builder.Entity<AiConversation>(entity =>
        {
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(c => c.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(c => c.UserId);
            entity.Property(c => c.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");
            entity.Property(c => c.LastMessageAt).HasDefaultValueSql("now() at time zone 'utc'");

            entity.HasMany(c => c.Messages)
                .WithOne()
                .HasForeignKey(m => m.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AiConversationMessage>(entity =>
        {
            entity.HasIndex(m => m.ConversationId);
            entity.Property(m => m.Role).HasMaxLength(20).IsRequired();
            entity.Property(m => m.ContentJson).HasColumnType("text").IsRequired();
            entity.Property(m => m.DisplayText).HasColumnType("text");
            entity.Property(m => m.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");
        });

        builder.Entity<AiUsageLog>(entity =>
        {
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(l => l.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(l => l.UserId);
            entity.HasIndex(l => l.RequestedAt);
            entity.Property(l => l.RequestedAt).HasDefaultValueSql("now() at time zone 'utc'");
            entity.Property(l => l.Model).HasMaxLength(100);
        });

        builder.Entity<ClientAllergy>(entity =>
        {
            entity.HasQueryFilter(a => !a.IsDeleted);

            entity.HasOne<Client>()
                .WithMany(c => c.Allergies)
                .HasForeignKey(a => a.ClientId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(a => a.ClientId);

            entity.Property(a => a.Severity).HasConversion<string>();
            entity.Property(a => a.AllergyType).HasConversion<string>();
            entity.Property(a => a.Name).HasMaxLength(100);
            entity.Property(a => a.IsDeleted).HasDefaultValue(false);
            entity.Property(a => a.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");
        });

        builder.Entity<ClientMedication>(entity =>
        {
            entity.HasQueryFilter(m => !m.IsDeleted);

            entity.HasOne<Client>()
                .WithMany(c => c.Medications)
                .HasForeignKey(m => m.ClientId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(m => m.ClientId);

            entity.Property(m => m.Name).HasMaxLength(100);
            entity.Property(m => m.Dosage).HasMaxLength(100);
            entity.Property(m => m.Frequency).HasMaxLength(100);
            entity.Property(m => m.PrescribedFor).HasMaxLength(200);
            entity.Property(m => m.IsDeleted).HasDefaultValue(false);
            entity.Property(m => m.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");
        });

        builder.Entity<ClientCondition>(entity =>
        {
            entity.HasQueryFilter(c => !c.IsDeleted);

            entity.HasOne<Client>()
                .WithMany(c => c.Conditions)
                .HasForeignKey(c => c.ClientId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(c => c.ClientId);

            entity.Property(c => c.Status).HasConversion<string>();
            entity.Property(c => c.Name).HasMaxLength(100);
            entity.Property(c => c.Code).HasMaxLength(20);
            entity.Property(c => c.Notes).HasColumnType("text");
            if (converter is not null) entity.Property(c => c.Notes).HasConversion(converter);
            entity.Property(c => c.IsDeleted).HasDefaultValue(false);
            entity.Property(c => c.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");
        });

        builder.Entity<ClientDietaryRestriction>(entity =>
        {
            entity.HasQueryFilter(dr => !dr.IsDeleted);

            entity.HasOne<Client>()
                .WithMany(c => c.DietaryRestrictions)
                .HasForeignKey(dr => dr.ClientId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(dr => dr.ClientId);

            entity.Property(dr => dr.RestrictionType).HasConversion<string>();
            entity.Property(dr => dr.Notes).HasColumnType("text");
            if (converter is not null) entity.Property(dr => dr.Notes).HasConversion(converter);
            entity.Property(dr => dr.IsDeleted).HasDefaultValue(false);
            entity.Property(dr => dr.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");
        });

        builder.Entity<Condition>(entity =>
        {
            entity.HasQueryFilter(c => !c.IsDeleted);

            entity.HasIndex(c => c.Name).IsUnique();

            entity.Property(c => c.Name).HasMaxLength(200);
            entity.Property(c => c.IcdCode).HasMaxLength(20);
            entity.Property(c => c.Category).HasMaxLength(100);
            entity.Property(c => c.IsDeleted).HasDefaultValue(false);
            entity.Property(c => c.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");
        });

        builder.Entity<PractitionerSchedule>(entity =>
        {
            entity.HasQueryFilter(s => !s.IsDeleted);

            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(s => new { s.UserId, s.DayOfWeek });

            entity.Property(s => s.DayOfWeek).HasConversion<string>();
            entity.Property(s => s.IsDeleted).HasDefaultValue(false);
            entity.Property(s => s.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");
        });

        builder.Entity<Allergen>(entity =>
        {
            entity.HasQueryFilter(a => !a.IsDeleted);
            entity.Property(a => a.Name).HasMaxLength(200);
            entity.Property(a => a.Category).HasMaxLength(50);
            entity.HasIndex(a => a.Name).IsUnique();
            entity.Property(a => a.IsDeleted).HasDefaultValue(false);
            entity.Property(a => a.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");
        });

        builder.Entity<AllergenWarningOverride>(entity =>
        {
            entity.HasOne<MealPlan>()
                .WithMany(mp => mp.AllergenWarningOverrides)
                .HasForeignKey(o => o.MealPlanId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(o => new { o.MealPlanId, o.FoodName, o.AllergenCategory }).IsUnique();

            entity.Property(o => o.AllergenCategory).HasConversion<string>();
            entity.Property(o => o.FoodName).HasMaxLength(200);
            entity.Property(o => o.OverrideNote).HasColumnType("text");
            entity.Property(o => o.AcknowledgedByUserId).HasMaxLength(450);
            entity.Property(o => o.AcknowledgedAt).HasDefaultValueSql("now() at time zone 'utc'");
        });

        builder.Entity<IntakeForm>(entity =>
        {
            entity.HasQueryFilter(f => !f.IsDeleted);

            entity.HasIndex(f => f.Token).IsUnique();

            entity.HasOne<Client>()
                .WithMany()
                .HasForeignKey(f => f.ClientId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<Appointment>()
                .WithMany()
                .HasForeignKey(f => f.AppointmentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(f => f.ReviewedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(f => f.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(f => f.Status).HasConversion<string>();
            entity.Property(f => f.Token).HasMaxLength(50).IsRequired();
            entity.Property(f => f.ClientEmail).HasMaxLength(256).IsRequired();
            entity.Property(f => f.CreatedByUserId).HasMaxLength(450);
            entity.Property(f => f.ReviewedByUserId).HasMaxLength(450);
            entity.Property(f => f.IsDeleted).HasDefaultValue(false);
            entity.Property(f => f.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");

            entity.HasMany(f => f.Responses)
                .WithOne()
                .HasForeignKey(r => r.IntakeFormId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<IntakeFormResponse>(entity =>
        {
            entity.HasIndex(r => r.IntakeFormId);

            entity.Property(r => r.SectionKey).HasMaxLength(50).IsRequired();
            entity.Property(r => r.FieldKey).HasMaxLength(50).IsRequired();
            entity.Property(r => r.Value).HasColumnType("text").IsRequired();
        });

        builder.Entity<PractitionerTimeBlock>(entity =>
        {
            entity.HasQueryFilter(tb => !tb.IsDeleted);

            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(tb => tb.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(tb => new { tb.UserId, tb.Date });

            entity.Property(tb => tb.BlockType).HasConversion<string>();
            entity.Property(tb => tb.Notes).HasColumnType("text");
            if (converter is not null) entity.Property(tb => tb.Notes).HasConversion(converter);
            entity.Property(tb => tb.IsDeleted).HasDefaultValue(false);
            entity.Property(tb => tb.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");
        });

        builder.Entity<Medication>(entity =>
        {
            entity.HasQueryFilter(m => !m.IsDeleted);
            entity.HasIndex(m => m.Name);
            entity.Property(m => m.Name).HasMaxLength(200);
            entity.Property(m => m.GenericName).HasMaxLength(200);
            entity.Property(m => m.IsDeleted).HasDefaultValue(false);
            entity.Property(m => m.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");
        });

        builder.Entity<AppointmentReminder>(entity =>
        {
            entity.HasQueryFilter(r => !r.IsDeleted);

            entity.HasOne<Appointment>()
                .WithMany()
                .HasForeignKey(r => r.AppointmentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(r => new { r.AppointmentId, r.ReminderType, r.ScheduledFor }).IsUnique();

            entity.Property(r => r.ReminderType).HasConversion<string>();
            entity.Property(r => r.Status).HasConversion<string>();
            entity.Property(r => r.FailureReason).HasMaxLength(500);
            entity.Property(r => r.IsDeleted).HasDefaultValue(false);
            entity.Property(r => r.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");
        });

        builder.Entity<SessionNote>(entity =>
        {
            entity.HasQueryFilter(sn => !sn.IsDeleted);

            entity.HasIndex(sn => sn.AppointmentId).IsUnique();
            entity.HasIndex(sn => sn.ClientId);

            entity.HasOne<Client>()
                .WithMany()
                .HasForeignKey(sn => sn.ClientId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<Appointment>()
                .WithMany()
                .HasForeignKey(sn => sn.AppointmentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(sn => sn.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(sn => sn.CreatedByUserId).HasMaxLength(450);
            entity.Property(sn => sn.Notes).HasColumnType("text");
            entity.Property(sn => sn.MeasurementsTaken).HasColumnType("text");
            entity.Property(sn => sn.PlanAdjustments).HasColumnType("text");
            entity.Property(sn => sn.FollowUpActions).HasColumnType("text");
            if (converter is not null)
            {
                entity.Property(sn => sn.Notes).HasConversion(converter);
                entity.Property(sn => sn.MeasurementsTaken).HasConversion(converter);
                entity.Property(sn => sn.PlanAdjustments).HasConversion(converter);
                entity.Property(sn => sn.FollowUpActions).HasConversion(converter);
            }
            entity.Property(sn => sn.IsDeleted).HasDefaultValue(false);
            entity.Property(sn => sn.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");
        });
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<AuditLogEntry>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    "AuditLogEntry records are immutable and cannot be modified or deleted.");
            }
        }

        return await base.SaveChangesAsync(cancellationToken);
    }
}
