# Database & EF Core

## PostgreSQL Setup

- **Engine**: PostgreSQL 17 (via Docker `postgres:17` image)
- **Host port**: 7103 (mapped from container's 5432)
- **Default credentials**: `nutrir` / `nutrir_dev` (see `.env.example`)
- **Database name**: `nutrir`

## AppDbContext

Located at `src/Nutrir.Infrastructure/Data/AppDbContext.cs`.

```csharp
public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
}
```

Key points:
- Inherits `IdentityDbContext<ApplicationUser>` — provides all ASP.NET Identity tables
- Uses primary constructor syntax (.NET 8+)
- `ApplicationUser` (`src/Nutrir.Core/Entities/ApplicationUser.cs`) extends `IdentityUser` — currently has no additional properties
- Registered via `AddInfrastructure()` in `DependencyInjection.cs` using Npgsql

## Migration Commands

All commands run from the repository root:

```bash
# Add a new migration
dotnet ef migrations add <MigrationName> \
    --project src/Nutrir.Infrastructure \
    --startup-project src/Nutrir.Web

# Apply migrations to database
dotnet ef database update \
    --project src/Nutrir.Infrastructure \
    --startup-project src/Nutrir.Web

# Remove last migration (if not applied)
dotnet ef migrations remove \
    --project src/Nutrir.Infrastructure \
    --startup-project src/Nutrir.Web
```

The `--project` flag targets Infrastructure (where `AppDbContext` and migrations live). The `--startup-project` flag targets Web (where `Program.cs` and connection strings live).

## Current Schema

### Migration: `InitialIdentity` (2026-02-21)

The initial migration creates the standard ASP.NET Identity tables:

| Table | Purpose |
|-------|---------|
| `AspNetUsers` | User accounts (extends `IdentityUser`) |
| `AspNetRoles` | Role definitions |
| `AspNetUserRoles` | User-role assignments |
| `AspNetUserClaims` | User claims |
| `AspNetRoleClaims` | Role claims |
| `AspNetUserLogins` | External login providers (OAuth) |
| `AspNetUserTokens` | Authentication tokens |

Migration files are in `src/Nutrir.Infrastructure/Migrations/`.

## Conventions for Future Entities

Per the project's compliance requirements (see CLAUDE.md), new entities should follow these patterns:

### Soft Delete

All client-related data must support soft delete:

```csharp
public bool IsDeleted { get; set; }
public DateTime? DeletedAt { get; set; }
```

Never hard-delete client data without an explicit purge workflow.

### Audit Fields

Entities that need audit tracking should include:

```csharp
public DateTime CreatedAt { get; set; }
public string CreatedBy { get; set; }
public DateTime? ModifiedAt { get; set; }
public string? ModifiedBy { get; set; }
```

### Audit Log

An append-only audit log table should track who viewed/edited what record and when. This is a v1 compliance requirement.

### Configuration

Entity configurations should use `IEntityTypeConfiguration<T>` in separate files under `src/Nutrir.Infrastructure/Data/Configurations/` to keep `AppDbContext` clean.
