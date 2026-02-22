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
    }
}
