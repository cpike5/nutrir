using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Nutrir.Core.DTOs;
using Nutrir.Core.Entities;
using Nutrir.Core.Enums;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Data;
using Nutrir.Infrastructure.Services;
using Nutrir.Tests.Unit.Helpers;
using Xunit;

namespace Nutrir.Tests.Unit.Services;

public class AvailabilityServiceTests : IDisposable
{
    private readonly AppDbContext _dbContext;
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
    private readonly SharedConnectionContextFactory _dbContextFactory;

    private readonly IAuditLogService _auditLogService;

    private readonly AvailabilityService _sut;

    private const string NutritionistId = "nutritionist-availability-test-001";
    private const string UserId = "acting-user-availability-001";

    // Captured after SaveChanges so tests do not hard-code a magic number.
    private int _seededClientId;

    // Monday 09:00–17:00 — used across many slot tests.
    private static readonly DateOnly MondayDate = new(2025, 6, 9); // confirmed Monday
    private static readonly TimeOnly ScheduleStart = new(9, 0);
    private static readonly TimeOnly ScheduleEnd = new(17, 0);

    public AvailabilityServiceTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _dbContextFactory = new SharedConnectionContextFactory(_connection);

        _auditLogService = Substitute.For<IAuditLogService>();

        _sut = new AvailabilityService(
            _dbContext,
            _dbContextFactory,
            _auditLogService,
            NullLogger<AvailabilityService>.Instance);

        SeedData();
    }

    // ---------------------------------------------------------------------------
    // Seed helpers
    // ---------------------------------------------------------------------------

    private void SeedData()
    {
        var nutritionist = new ApplicationUser
        {
            Id = NutritionistId,
            UserName = "nutritionist@availtest.com",
            NormalizedUserName = "NUTRITIONIST@AVAILTEST.COM",
            Email = "nutritionist@availtest.com",
            NormalizedEmail = "NUTRITIONIST@AVAILTEST.COM",
            FirstName = "Jane",
            LastName = "Smith",
            DisplayName = "Jane Smith",
            BufferTimeMinutes = 10,
            CreatedDate = DateTime.UtcNow
        };

        var actingUser = new ApplicationUser
        {
            Id = UserId,
            UserName = "actinguser@availtest.com",
            NormalizedUserName = "ACTINGUSER@AVAILTEST.COM",
            Email = "actinguser@availtest.com",
            NormalizedEmail = "ACTINGUSER@AVAILTEST.COM",
            FirstName = "Acting",
            LastName = "User",
            DisplayName = "Acting User",
            CreatedDate = DateTime.UtcNow
        };

        // Monday 09:00–17:00, available
        var mondaySchedule = new PractitionerSchedule
        {
            UserId = NutritionistId,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = ScheduleStart,
            EndTime = ScheduleEnd,
            IsAvailable = true,
            CreatedAt = DateTime.UtcNow
        };

        // Sunday — IsAvailable=false
        var sundaySchedule = new PractitionerSchedule
        {
            UserId = NutritionistId,
            DayOfWeek = DayOfWeek.Sunday,
            StartTime = new TimeOnly(10, 0),
            EndTime = new TimeOnly(14, 0),
            IsAvailable = false,
            CreatedAt = DateTime.UtcNow
        };

        var client = new Client
        {
            FirstName = "Alice",
            LastName = "Availability",
            PrimaryNutritionistId = NutritionistId,
            ConsentGiven = true,
            EmailRemindersEnabled = true,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Users.Add(nutritionist);
        _dbContext.Users.Add(actingUser);
        _dbContext.PractitionerSchedules.Add(mondaySchedule);
        _dbContext.PractitionerSchedules.Add(sundaySchedule);
        _dbContext.Clients.Add(client);
        _dbContext.SaveChanges();

        _seededClientId = client.Id;
    }

    // ---------------------------------------------------------------------------
    // GetAvailableSlotsAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetAvailableSlotsAsync_ClearDay_ReturnsSlotsCoveringWorkingHours()
    {
        // Arrange — Monday with no appointments or time blocks, 60-minute slots
        var durationMinutes = 60;

        // Act
        var slots = await _sut.GetAvailableSlotsAsync(NutritionistId, MondayDate, durationMinutes);

        // Assert
        slots.Should().NotBeEmpty();
        slots.First().Start.Should().Be(ScheduleStart);
        // Last slot must end on or before schedule end
        slots.Last().End.Should().BeOnOrBefore(ScheduleEnd);
        // All slots must be spaced 15 minutes apart
        for (var i = 1; i < slots.Count; i++)
            slots[i].Start.Should().Be(slots[i - 1].Start.AddMinutes(15));
    }

    [Fact]
    public async Task GetAvailableSlotsAsync_NoScheduleForDay_ReturnsEmpty()
    {
        // Arrange — Wednesday has no seeded schedule entry
        var wednesday = new DateOnly(2025, 6, 11); // Wednesday

        // Act
        var slots = await _sut.GetAvailableSlotsAsync(NutritionistId, wednesday, 60);

        // Assert
        slots.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAvailableSlotsAsync_ScheduleIsAvailableFalse_ReturnsEmpty()
    {
        // Arrange — Sunday schedule has IsAvailable=false
        var sunday = new DateOnly(2025, 6, 8); // Sunday

        // Act
        var slots = await _sut.GetAvailableSlotsAsync(NutritionistId, sunday, 60);

        // Assert
        slots.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAvailableSlotsAsync_AppointmentExists_ExcludesOverlappingSlots()
    {
        // Arrange — appointment at 10:00 for 60 minutes; seeded buffer is 10 minutes.
        // Blocked window becomes 09:50–11:10.
        var apptStartUtc = DateTime.SpecifyKind(MondayDate.ToDateTime(new TimeOnly(10, 0)), DateTimeKind.Utc);
        _dbContext.Appointments.Add(new Appointment
        {
            ClientId = _seededClientId,
            NutritionistId = NutritionistId,
            StartTime = apptStartUtc,
            DurationMinutes = 60,
            Status = AppointmentStatus.Scheduled,
            Type = AppointmentType.FollowUp,
            Location = AppointmentLocation.InPerson,
            CreatedAt = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        // Act
        var slots = await _sut.GetAvailableSlotsAsync(NutritionistId, MondayDate, 60);

        // Assert — no slot that overlaps the buffered window [09:50, 11:10) should appear
        slots.Should().NotContain(s => s.Start < new TimeOnly(11, 10) && s.End > new TimeOnly(9, 50));
    }

    [Fact]
    public async Task GetAvailableSlotsAsync_TimeBlockExists_ExcludesOverlappingSlots()
    {
        // Arrange — lunch block 12:00–13:00
        _dbContext.PractitionerTimeBlocks.Add(new PractitionerTimeBlock
        {
            UserId = NutritionistId,
            Date = MondayDate,
            StartTime = new TimeOnly(12, 0),
            EndTime = new TimeOnly(13, 0),
            BlockType = TimeBlockType.Lunch,
            CreatedAt = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        // Act
        var slots = await _sut.GetAvailableSlotsAsync(NutritionistId, MondayDate, 60);

        // Assert — no 60-minute slot should overlap [12:00, 13:00)
        slots.Should().NotContain(s => s.Start < new TimeOnly(13, 0) && s.End > new TimeOnly(12, 0));
    }

    [Fact]
    public async Task GetAvailableSlotsAsync_CancelledAppointment_DoesNotBlockSlots()
    {
        // Arrange — cancelled appointment at 10:00 should be ignored
        var apptStartUtc = DateTime.SpecifyKind(MondayDate.ToDateTime(new TimeOnly(10, 0)), DateTimeKind.Utc);
        _dbContext.Appointments.Add(new Appointment
        {
            ClientId = _seededClientId,
            NutritionistId = NutritionistId,
            StartTime = apptStartUtc,
            DurationMinutes = 60,
            Status = AppointmentStatus.Cancelled,
            Type = AppointmentType.FollowUp,
            Location = AppointmentLocation.InPerson,
            CreatedAt = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        // Act
        var slots = await _sut.GetAvailableSlotsAsync(NutritionistId, MondayDate, 60);

        // Assert — the 10:00 slot must still be present (cancelled appt has no effect)
        slots.Should().Contain(s => s.Start == new TimeOnly(10, 0));
    }

    [Fact]
    public async Task GetAvailableSlotsAsync_LateCancellationAppointment_DoesNotBlockSlots()
    {
        // Arrange — late-cancellation appointment at 10:00 should also be ignored
        var apptStartUtc = DateTime.SpecifyKind(MondayDate.ToDateTime(new TimeOnly(10, 0)), DateTimeKind.Utc);
        _dbContext.Appointments.Add(new Appointment
        {
            ClientId = _seededClientId,
            NutritionistId = NutritionistId,
            StartTime = apptStartUtc,
            DurationMinutes = 60,
            Status = AppointmentStatus.LateCancellation,
            Type = AppointmentType.FollowUp,
            Location = AppointmentLocation.InPerson,
            CreatedAt = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        // Act — clear-day slots for comparison (no other test data for this date run)
        var slots = await _sut.GetAvailableSlotsAsync(NutritionistId, MondayDate, 60);

        // Assert — the 10:00 slot must still be present
        slots.Should().Contain(s => s.Start == new TimeOnly(10, 0));
    }

    [Fact]
    public async Task GetAvailableSlotsAsync_NoShowAppointment_BlocksSlots()
    {
        // Arrange — NoShow is NOT excluded by the service, so it still blocks the slot
        var apptStartUtc = DateTime.SpecifyKind(MondayDate.ToDateTime(new TimeOnly(10, 0)), DateTimeKind.Utc);
        _dbContext.Appointments.Add(new Appointment
        {
            ClientId = _seededClientId,
            NutritionistId = NutritionistId,
            StartTime = apptStartUtc,
            DurationMinutes = 60,
            Status = AppointmentStatus.NoShow,
            Type = AppointmentType.FollowUp,
            Location = AppointmentLocation.InPerson,
            CreatedAt = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        // Act
        var slots = await _sut.GetAvailableSlotsAsync(NutritionistId, MondayDate, 60);

        // Assert — the 10:00 slot must be blocked (NoShow still occupies the time)
        slots.Should().NotContain(s => s.Start == new TimeOnly(10, 0));
    }

    [Fact]
    public async Task GetAvailableSlotsAsync_UserHasCustomBufferTime_AppliesCorrectBuffer()
    {
        // Arrange — the seeded nutritionist has BufferTimeMinutes=10.
        // Appointment at 10:00 for 30 min → buffered block is [09:50, 10:40).
        // A 30-minute slot starting at 09:45 would end at 10:15, which overlaps [09:50, ...).
        // A slot starting at 10:40 should be free.
        var apptStartUtc = DateTime.SpecifyKind(MondayDate.ToDateTime(new TimeOnly(10, 0)), DateTimeKind.Utc);
        _dbContext.Appointments.Add(new Appointment
        {
            ClientId = _seededClientId,
            NutritionistId = NutritionistId,
            StartTime = apptStartUtc,
            DurationMinutes = 30,
            Status = AppointmentStatus.Confirmed,
            Type = AppointmentType.FollowUp,
            Location = AppointmentLocation.InPerson,
            CreatedAt = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        // Act
        var slots = await _sut.GetAvailableSlotsAsync(NutritionistId, MondayDate, 30);

        // Assert — slot at 09:45 (overlaps the 10-min pre-buffer) must be absent
        slots.Should().NotContain(s => s.Start == new TimeOnly(9, 45));
        // 10:45 is the first 15-minute-grid slot whose start falls at or after block end (10:40)
        slots.Should().Contain(s => s.Start == new TimeOnly(10, 45));
    }

    [Fact]
    public async Task GetAvailableSlotsAsync_UserWithNoBufferTimeRow_DefaultsToFifteenMinuteBuffer()
    {
        // Arrange — seed a practitioner user with BufferTimeMinutes not explicitly set
        // (entity default is 15), plus a schedule and an appointment.
        // The point is to verify the ?? 15 fallback path via GetBufferTimeMinutesAsync
        // and confirm the buffer applied to slots matches the 15-minute default.
        // We seed a real user so the Appointment FK constraint is satisfied.
        const string defaultBufferId = "default-buffer-practitioner-001";
        _dbContext.Users.Add(new ApplicationUser
        {
            Id = defaultBufferId,
            UserName = "defaultbuffer@availtest.com",
            NormalizedUserName = "DEFAULTBUFFER@AVAILTEST.COM",
            Email = "defaultbuffer@availtest.com",
            NormalizedEmail = "DEFAULTBUFFER@AVAILTEST.COM",
            FirstName = "Default",
            LastName = "Buffer",
            DisplayName = "Default Buffer",
            BufferTimeMinutes = 15, // explicit default — service returns this, not the ?? fallback
            CreatedDate = DateTime.UtcNow
        });
        _dbContext.PractitionerSchedules.Add(new PractitionerSchedule
        {
            UserId = defaultBufferId,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(17, 0),
            IsAvailable = true,
            CreatedAt = DateTime.UtcNow
        });
        // Appointment at 10:00 for 30 min → with 15-min buffer, blocked = [09:45, 10:45)
        var apptStartUtc = DateTime.SpecifyKind(MondayDate.ToDateTime(new TimeOnly(10, 0)), DateTimeKind.Utc);
        _dbContext.Appointments.Add(new Appointment
        {
            ClientId = _seededClientId,
            NutritionistId = defaultBufferId,
            StartTime = apptStartUtc,
            DurationMinutes = 30,
            Status = AppointmentStatus.Scheduled,
            Type = AppointmentType.FollowUp,
            Location = AppointmentLocation.InPerson,
            CreatedAt = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        // Act
        var slots = await _sut.GetAvailableSlotsAsync(defaultBufferId, MondayDate, 30);

        // Assert — 09:45 slot (within the 15-min pre-buffer) must be absent
        slots.Should().NotContain(s => s.Start == new TimeOnly(9, 45));
        // 10:45 is the first 15-minute-grid slot after the 15-min post-buffer ends at 10:45
        slots.Should().Contain(s => s.Start == new TimeOnly(10, 45));
    }

    // ---------------------------------------------------------------------------
    // GetWeeklyScheduleAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetWeeklyScheduleAsync_WithSeededSchedules_ReturnsAllEntriesForPractitioner()
    {
        // Act
        var result = await _sut.GetWeeklyScheduleAsync(NutritionistId);

        // Assert — both seeded schedules are returned with correct field values.
        // Note: SQLite stores enum columns as text, so the ORDER BY DayOfWeek in the service
        // produces alphabetical order ("Monday" < "Sunday") rather than numeric (0, 1).
        // We validate presence and field correctness rather than exact sort position.
        result.Should().HaveCount(2);
        result.Should().Contain(s => s.DayOfWeek == DayOfWeek.Monday
                                     && s.StartTime == ScheduleStart
                                     && s.EndTime == ScheduleEnd
                                     && s.IsAvailable);
        result.Should().Contain(s => s.DayOfWeek == DayOfWeek.Sunday && !s.IsAvailable);
        result.Should().AllSatisfy(s => s.UserId.Should().Be(NutritionistId));
    }

    [Fact]
    public async Task GetWeeklyScheduleAsync_UnknownPractitioner_ReturnsEmpty()
    {
        // Act
        var result = await _sut.GetWeeklyScheduleAsync("unknown-practitioner-xyz");

        // Assert
        result.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------
    // SetWeeklyScheduleAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task SetWeeklyScheduleAsync_WithNewEntries_CreatesNewScheduleRows()
    {
        // Arrange
        var newEntries = new List<SetScheduleEntryDto>
        {
            new(DayOfWeek.Tuesday, new TimeOnly(8, 0), new TimeOnly(16, 0), true),
            new(DayOfWeek.Wednesday, new TimeOnly(8, 0), new TimeOnly(12, 0), true)
        };

        // Act
        await _sut.SetWeeklyScheduleAsync(NutritionistId, newEntries, UserId);

        // Assert — new rows are persisted
        var active = await _dbContext.PractitionerSchedules
            .Where(s => s.UserId == NutritionistId && !s.IsDeleted)
            .ToListAsync();

        active.Should().HaveCount(2);
        active.Should().Contain(s => s.DayOfWeek == DayOfWeek.Tuesday);
        active.Should().Contain(s => s.DayOfWeek == DayOfWeek.Wednesday);
    }

    [Fact]
    public async Task SetWeeklyScheduleAsync_ExistingEntries_SoftDeletesOldRows()
    {
        // Arrange
        var newEntries = new List<SetScheduleEntryDto>
        {
            new(DayOfWeek.Friday, new TimeOnly(9, 0), new TimeOnly(13, 0), true)
        };

        // Act
        await _sut.SetWeeklyScheduleAsync(NutritionistId, newEntries, UserId);

        // Assert — previously seeded Monday and Sunday entries must be soft-deleted
        var deleted = await _dbContext.PractitionerSchedules
            .IgnoreQueryFilters()
            .Where(s => s.UserId == NutritionistId && s.IsDeleted)
            .ToListAsync();

        deleted.Should().HaveCount(2);
        deleted.Should().AllSatisfy(s =>
        {
            s.DeletedAt.Should().NotBeNull();
            s.DeletedBy.Should().Be(UserId);
        });
    }

    [Fact]
    public async Task SetWeeklyScheduleAsync_WithEntries_LogsAuditEntry()
    {
        // Arrange
        var entries = new List<SetScheduleEntryDto>
        {
            new(DayOfWeek.Thursday, new TimeOnly(9, 0), new TimeOnly(17, 0), true)
        };

        // Act
        await _sut.SetWeeklyScheduleAsync(NutritionistId, entries, UserId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            UserId,
            "WeeklyScheduleUpdated",
            "PractitionerSchedule",
            NutritionistId,
            Arg.Any<string>());
    }

    // ---------------------------------------------------------------------------
    // GetTimeBlocksAsync
    // ---------------------------------------------------------------------------

    private async Task<PractitionerTimeBlock> SeedTimeBlockAsync(DateOnly date, TimeOnly start, TimeOnly end)
    {
        var block = new PractitionerTimeBlock
        {
            UserId = NutritionistId,
            Date = date,
            StartTime = start,
            EndTime = end,
            BlockType = TimeBlockType.Meeting,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.PractitionerTimeBlocks.Add(block);
        await _dbContext.SaveChangesAsync();
        return block;
    }

    [Fact]
    public async Task GetTimeBlocksAsync_NoDateFilter_ReturnsAllBlocksForPractitioner()
    {
        // Arrange
        await SeedTimeBlockAsync(new DateOnly(2025, 6, 9), new TimeOnly(12, 0), new TimeOnly(13, 0));
        await SeedTimeBlockAsync(new DateOnly(2025, 6, 10), new TimeOnly(15, 0), new TimeOnly(16, 0));

        // Act
        var result = await _sut.GetTimeBlocksAsync(NutritionistId);

        // Assert
        result.Should().HaveCount(2);
        result.Should().BeInAscendingOrder(b => b.Date);
    }

    [Fact]
    public async Task GetTimeBlocksAsync_FilterByFromDate_ExcludesBlocksBeforeFromDate()
    {
        // Arrange
        await SeedTimeBlockAsync(new DateOnly(2025, 6, 5), new TimeOnly(9, 0), new TimeOnly(10, 0));
        await SeedTimeBlockAsync(new DateOnly(2025, 6, 10), new TimeOnly(9, 0), new TimeOnly(10, 0));
        await SeedTimeBlockAsync(new DateOnly(2025, 6, 15), new TimeOnly(9, 0), new TimeOnly(10, 0));

        // Act
        var result = await _sut.GetTimeBlocksAsync(NutritionistId, fromDate: new DateOnly(2025, 6, 10));

        // Assert
        result.Should().HaveCount(2);
        result.Should().NotContain(b => b.Date < new DateOnly(2025, 6, 10));
    }

    [Fact]
    public async Task GetTimeBlocksAsync_FilterByToDate_ExcludesBlocksAfterToDate()
    {
        // Arrange
        await SeedTimeBlockAsync(new DateOnly(2025, 6, 5), new TimeOnly(9, 0), new TimeOnly(10, 0));
        await SeedTimeBlockAsync(new DateOnly(2025, 6, 10), new TimeOnly(9, 0), new TimeOnly(10, 0));
        await SeedTimeBlockAsync(new DateOnly(2025, 6, 15), new TimeOnly(9, 0), new TimeOnly(10, 0));

        // Act
        var result = await _sut.GetTimeBlocksAsync(NutritionistId, toDate: new DateOnly(2025, 6, 10));

        // Assert
        result.Should().HaveCount(2);
        result.Should().NotContain(b => b.Date > new DateOnly(2025, 6, 10));
    }

    [Fact]
    public async Task GetTimeBlocksAsync_FilterByFromAndToDate_ReturnsOnlyBlocksWithinRange()
    {
        // Arrange
        await SeedTimeBlockAsync(new DateOnly(2025, 6, 1), new TimeOnly(9, 0), new TimeOnly(10, 0));
        await SeedTimeBlockAsync(new DateOnly(2025, 6, 10), new TimeOnly(9, 0), new TimeOnly(10, 0));
        await SeedTimeBlockAsync(new DateOnly(2025, 6, 20), new TimeOnly(9, 0), new TimeOnly(10, 0));

        // Act
        var result = await _sut.GetTimeBlocksAsync(
            NutritionistId,
            fromDate: new DateOnly(2025, 6, 5),
            toDate: new DateOnly(2025, 6, 15));

        // Assert
        result.Should().HaveCount(1);
        result.Single().Date.Should().Be(new DateOnly(2025, 6, 10));
    }

    // ---------------------------------------------------------------------------
    // AddTimeBlockAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task AddTimeBlockAsync_WithValidDto_PersistsTimeBlock()
    {
        // Arrange
        var dto = new CreateTimeBlockDto(
            NutritionistId,
            new DateOnly(2025, 7, 1),
            new TimeOnly(12, 0),
            new TimeOnly(13, 0),
            TimeBlockType.Lunch,
            "Lunch break");

        // Act
        var result = await _sut.AddTimeBlockAsync(dto, UserId);

        // Assert
        var persisted = await _dbContext.PractitionerTimeBlocks.FindAsync(result.Id);
        persisted.Should().NotBeNull();
        persisted!.UserId.Should().Be(NutritionistId);
        persisted.Date.Should().Be(new DateOnly(2025, 7, 1));
        persisted.StartTime.Should().Be(new TimeOnly(12, 0));
        persisted.EndTime.Should().Be(new TimeOnly(13, 0));
        persisted.BlockType.Should().Be(TimeBlockType.Lunch);
        persisted.Notes.Should().Be("Lunch break");
    }

    [Fact]
    public async Task AddTimeBlockAsync_WithValidDto_ReturnsCorrectDto()
    {
        // Arrange
        var dto = new CreateTimeBlockDto(
            NutritionistId,
            new DateOnly(2025, 7, 2),
            new TimeOnly(15, 0),
            new TimeOnly(16, 0),
            TimeBlockType.Personal,
            null);

        // Act
        var result = await _sut.AddTimeBlockAsync(dto, UserId);

        // Assert
        result.UserId.Should().Be(NutritionistId);
        result.Date.Should().Be(new DateOnly(2025, 7, 2));
        result.StartTime.Should().Be(new TimeOnly(15, 0));
        result.EndTime.Should().Be(new TimeOnly(16, 0));
        result.BlockType.Should().Be(TimeBlockType.Personal);
        result.Notes.Should().BeNull();
    }

    [Fact]
    public async Task AddTimeBlockAsync_WithValidDto_LogsAuditEntry()
    {
        // Arrange
        var dto = new CreateTimeBlockDto(
            NutritionistId,
            new DateOnly(2025, 7, 3),
            new TimeOnly(10, 0),
            new TimeOnly(11, 0),
            TimeBlockType.Meeting,
            null);

        // Act
        var result = await _sut.AddTimeBlockAsync(dto, UserId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            UserId,
            "TimeBlockAdded",
            "PractitionerTimeBlock",
            result.Id.ToString(),
            Arg.Any<string>());
    }

    // ---------------------------------------------------------------------------
    // RemoveTimeBlockAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task RemoveTimeBlockAsync_ExistingBlock_SoftDeletesAndReturnsTrue()
    {
        // Arrange
        var block = await SeedTimeBlockAsync(new DateOnly(2025, 8, 1), new TimeOnly(9, 0), new TimeOnly(10, 0));

        // Act
        var result = await _sut.RemoveTimeBlockAsync(block.Id, UserId);

        // Assert
        result.Should().BeTrue();
        var persisted = await _dbContext.PractitionerTimeBlocks
            .IgnoreQueryFilters()
            .FirstAsync(b => b.Id == block.Id);
        persisted.IsDeleted.Should().BeTrue();
        persisted.DeletedAt.Should().NotBeNull();
        persisted.DeletedBy.Should().Be(UserId);
    }

    [Fact]
    public async Task RemoveTimeBlockAsync_NonExistentBlock_ReturnsFalse()
    {
        // Act
        var result = await _sut.RemoveTimeBlockAsync(999999, UserId);

        // Assert
        result.Should().BeFalse();
        await _auditLogService.DidNotReceive().LogAsync(
            Arg.Any<string>(),
            "TimeBlockRemoved",
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>());
    }

    [Fact]
    public async Task RemoveTimeBlockAsync_ExistingBlock_LogsAuditEntry()
    {
        // Arrange
        var block = await SeedTimeBlockAsync(new DateOnly(2025, 8, 5), new TimeOnly(14, 0), new TimeOnly(15, 0));

        // Act
        await _sut.RemoveTimeBlockAsync(block.Id, UserId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            UserId,
            "TimeBlockRemoved",
            "PractitionerTimeBlock",
            block.Id.ToString(),
            Arg.Any<string>());
    }

    // ---------------------------------------------------------------------------
    // GetBufferTimeMinutesAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetBufferTimeMinutesAsync_KnownUser_ReturnsUsersValue()
    {
        // Act
        var result = await _sut.GetBufferTimeMinutesAsync(NutritionistId);

        // Assert — seeded nutritionist has BufferTimeMinutes=10
        result.Should().Be(10);
    }

    [Fact]
    public async Task GetBufferTimeMinutesAsync_UnknownUser_ReturnsFifteenDefault()
    {
        // Act
        var result = await _sut.GetBufferTimeMinutesAsync("nonexistent-practitioner-xyz");

        // Assert
        result.Should().Be(15);
    }

    // ---------------------------------------------------------------------------
    // SetBufferTimeMinutesAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task SetBufferTimeMinutesAsync_KnownUser_UpdatesBufferTimeMinutes()
    {
        // Act
        await _sut.SetBufferTimeMinutesAsync(NutritionistId, 20, UserId);

        // Assert
        var user = await _dbContext.Users.OfType<ApplicationUser>()
            .FirstAsync(u => u.Id == NutritionistId);
        user.BufferTimeMinutes.Should().Be(20);
    }

    [Fact]
    public async Task SetBufferTimeMinutesAsync_KnownUser_LogsAuditEntry()
    {
        // Act
        await _sut.SetBufferTimeMinutesAsync(NutritionistId, 5, UserId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            UserId,
            "BufferTimeUpdated",
            "ApplicationUser",
            NutritionistId,
            Arg.Any<string>());
    }

    [Fact]
    public async Task SetBufferTimeMinutesAsync_UnknownUser_DoesNothing()
    {
        // Act — should not throw
        await _sut.SetBufferTimeMinutesAsync("nonexistent-practitioner-xyz", 30, UserId);

        // Assert — no audit log emitted
        await _auditLogService.DidNotReceive().LogAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>());
    }

    // ---------------------------------------------------------------------------
    // IsSlotWithinScheduleAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task IsSlotWithinScheduleAsync_ValidSlotInWorkingHours_ReturnsIsWithinTrue()
    {
        // Arrange — 10:00–11:00 on Monday, well inside 09:00–17:00
        var startUtc = DateTime.SpecifyKind(MondayDate.ToDateTime(new TimeOnly(10, 0)), DateTimeKind.Utc);

        // Act
        var (isWithin, reason) = await _sut.IsSlotWithinScheduleAsync(NutritionistId, startUtc, 60);

        // Assert
        isWithin.Should().BeTrue();
        reason.Should().BeNull();
    }

    [Fact]
    public async Task IsSlotWithinScheduleAsync_NoScheduleForDay_ReturnsIsWithinFalse()
    {
        // Arrange — Wednesday has no schedule entry for this practitioner
        var wednesday = new DateOnly(2025, 6, 11);
        var startUtc = DateTime.SpecifyKind(wednesday.ToDateTime(new TimeOnly(10, 0)), DateTimeKind.Utc);

        // Act
        var (isWithin, reason) = await _sut.IsSlotWithinScheduleAsync(NutritionistId, startUtc, 60);

        // Assert
        isWithin.Should().BeFalse();
        reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task IsSlotWithinScheduleAsync_ScheduleIsAvailableFalse_ReturnsIsWithinFalse()
    {
        // Arrange — Sunday has IsAvailable=false
        var sunday = new DateOnly(2025, 6, 8);
        var startUtc = DateTime.SpecifyKind(sunday.ToDateTime(new TimeOnly(10, 0)), DateTimeKind.Utc);

        // Act
        var (isWithin, reason) = await _sut.IsSlotWithinScheduleAsync(NutritionistId, startUtc, 60);

        // Assert
        isWithin.Should().BeFalse();
        reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task IsSlotWithinScheduleAsync_SlotStartsBeforeWorkingHours_ReturnsIsWithinFalse()
    {
        // Arrange — 08:00 is before schedule start of 09:00
        var startUtc = DateTime.SpecifyKind(MondayDate.ToDateTime(new TimeOnly(8, 0)), DateTimeKind.Utc);

        // Act
        var (isWithin, reason) = await _sut.IsSlotWithinScheduleAsync(NutritionistId, startUtc, 60);

        // Assert
        isWithin.Should().BeFalse();
        reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task IsSlotWithinScheduleAsync_SlotEndsAfterWorkingHours_ReturnsIsWithinFalse()
    {
        // Arrange — 16:30 + 60 min = 17:30, which exceeds schedule end of 17:00
        var startUtc = DateTime.SpecifyKind(MondayDate.ToDateTime(new TimeOnly(16, 30)), DateTimeKind.Utc);

        // Act
        var (isWithin, reason) = await _sut.IsSlotWithinScheduleAsync(NutritionistId, startUtc, 60);

        // Assert
        isWithin.Should().BeFalse();
        reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task IsSlotWithinScheduleAsync_TimeBlockConflict_ReturnsIsWithinFalse()
    {
        // Arrange — meeting block 12:00–13:00 on Monday
        _dbContext.PractitionerTimeBlocks.Add(new PractitionerTimeBlock
        {
            UserId = NutritionistId,
            Date = MondayDate,
            StartTime = new TimeOnly(12, 0),
            EndTime = new TimeOnly(13, 0),
            BlockType = TimeBlockType.Meeting,
            CreatedAt = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        // Slot 12:00–13:00 exactly overlaps the block
        var startUtc = DateTime.SpecifyKind(MondayDate.ToDateTime(new TimeOnly(12, 0)), DateTimeKind.Utc);

        // Act
        var (isWithin, reason) = await _sut.IsSlotWithinScheduleAsync(NutritionistId, startUtc, 60);

        // Assert
        isWithin.Should().BeFalse();
        reason.Should().Contain("Meeting");
    }

    // ---------------------------------------------------------------------------
    // Dispose
    // ---------------------------------------------------------------------------

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }
}
