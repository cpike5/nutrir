using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Nutrir.Core.DTOs;
using Nutrir.Core.Entities;
using Nutrir.Core.Enums;
using Nutrir.Core.Interfaces;
using Nutrir.Core.Models;
using Nutrir.Infrastructure.Configuration;
using Nutrir.Infrastructure.Data;
using Nutrir.Infrastructure.Services;
using Nutrir.Tests.Unit.Helpers;
using QuestPDF.Infrastructure;
using Xunit;

namespace Nutrir.Tests.Unit.Services;

public class ConsentFormServiceTests : IDisposable
{
    private readonly AppDbContext _dbContext;
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;

    private readonly IConsentFormTemplate _template;
    private readonly IConsentService _consentService;
    private readonly IAuditLogService _auditLogService;
    private readonly INotificationDispatcher _notificationDispatcher;
    private readonly string _tempStoragePath;

    private readonly ConsentFormService _sut;

    private const string NutritionistId = "nutritionist-consent-form-test-001";
    private const string UserId = "acting-user-consent-form-001";
    private const string TemplateVersion = "1.0";

    // Captured after SaveChanges so tests reference the real DB-assigned Ids.
    private int _seededClientId;
    private int _otherClientId;

    public ConsentFormServiceTests()
    {
        // QuestPDF requires a license type before generating any document.
        // Community license applies to open-source / non-commercial projects.
        QuestPDF.Settings.License = LicenseType.Community;

        (_dbContext, _connection) = TestDbContextFactory.Create();

        _template = Substitute.For<IConsentFormTemplate>();
        _consentService = Substitute.For<IConsentService>();
        _auditLogService = Substitute.For<IAuditLogService>();
        _notificationDispatcher = Substitute.For<INotificationDispatcher>();

        _template.Version.Returns(TemplateVersion);
        _template.Generate(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime>())
            .Returns(BuildConsentFormContent());

        _tempStoragePath = Path.Combine(Path.GetTempPath(), "consent-form-test-" + Guid.NewGuid());

        var options = Options.Create(new ConsentFormOptions
        {
            ScannedCopyStoragePath = _tempStoragePath,
            DocxTemplatePath = null // forces programmatic DOCX generation
        });

        _sut = new ConsentFormService(
            _dbContext,
            _template,
            _consentService,
            _auditLogService,
            _notificationDispatcher,
            options,
            NullLogger<ConsentFormService>.Instance);

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
            UserName = "nutritionist@consentformtest.com",
            NormalizedUserName = "NUTRITIONIST@CONSENTFORMTEST.COM",
            Email = "nutritionist@consentformtest.com",
            NormalizedEmail = "NUTRITIONIST@CONSENTFORMTEST.COM",
            FirstName = "Jane",
            LastName = "Smith",
            DisplayName = "Jane Smith",
            CreatedDate = DateTime.UtcNow
        };

        var client = new Client
        {
            FirstName = "Alice",
            LastName = "FormClient",
            PrimaryNutritionistId = NutritionistId,
            ConsentGiven = false,
            EmailRemindersEnabled = true,
            CreatedAt = DateTime.UtcNow
        };

        var otherClient = new Client
        {
            FirstName = "Other",
            LastName = "FormClient",
            PrimaryNutritionistId = NutritionistId,
            ConsentGiven = false,
            EmailRemindersEnabled = false,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Users.Add(nutritionist);
        _dbContext.Clients.Add(client);
        _dbContext.Clients.Add(otherClient);
        _dbContext.SaveChanges();

        _seededClientId = client.Id;
        _otherClientId = otherClient.Id;
    }

    private static ConsentFormContent BuildConsentFormContent() => new()
    {
        Title = "Client Consent Form",
        PracticeName = "Test Practice",
        FormVersion = TemplateVersion,
        ClientName = "Alice FormClient",
        PractitionerName = "Jane Smith",
        Date = DateTime.UtcNow,
        Sections =
        [
            new ConsentSection
            {
                Heading = "Scope of Services",
                Paragraphs = ["This agreement covers nutritional counselling services."]
            }
        ],
        SignatureBlockText = "By signing below, you agree to the terms."
    };

    private ConsentForm SeedConsentForm(
        ConsentSignatureMethod method = ConsentSignatureMethod.Physical,
        bool isSigned = false,
        DateTime? generatedAt = null)
    {
        var form = new ConsentForm
        {
            ClientId = _seededClientId,
            FormVersion = TemplateVersion,
            GeneratedAt = generatedAt ?? DateTime.UtcNow,
            GeneratedByUserId = UserId,
            SignatureMethod = method,
            IsSigned = isSigned
        };

        _dbContext.Set<ConsentForm>().Add(form);
        _dbContext.SaveChanges();
        return form;
    }

    // ---------------------------------------------------------------------------
    // GeneratePdfAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GeneratePdfAsync_WithValidClient_ReturnsNonEmptyByteArray()
    {
        // Act
        var result = await _sut.GeneratePdfAsync(_seededClientId, UserId);

        // Assert
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GeneratePdfAsync_WithValidClient_CallsTemplateGenerate()
    {
        // Act
        await _sut.GeneratePdfAsync(_seededClientId, UserId);

        // Assert — template receives the correct client and practitioner names
        _template.Received(1).Generate(
            "Alice FormClient",
            "Jane Smith",
            Arg.Any<DateTime>());
    }

    [Fact]
    public async Task GeneratePdfAsync_WithValidClient_CreatesConsentFormRecord()
    {
        // Act
        await _sut.GeneratePdfAsync(_seededClientId, UserId);

        // Assert
        var forms = await _dbContext.Set<ConsentForm>()
            .Where(f => f.ClientId == _seededClientId)
            .ToListAsync();

        forms.Should().ContainSingle(because: "EnsureFormRecordExistsAsync should create exactly one record");
    }

    [Fact]
    public async Task GeneratePdfAsync_WhenFormRecordAlreadyExists_DoesNotCreateDuplicate()
    {
        // Arrange — pre-existing form record
        SeedConsentForm();

        // Act
        await _sut.GeneratePdfAsync(_seededClientId, UserId);

        // Assert — still only one record
        var forms = await _dbContext.Set<ConsentForm>()
            .Where(f => f.ClientId == _seededClientId)
            .ToListAsync();

        forms.Should().ContainSingle(because: "EnsureFormRecordExistsAsync should not create a duplicate when one already exists");
    }

    [Fact]
    public async Task GeneratePdfAsync_WithValidClient_CallsAuditLog()
    {
        // Act
        await _sut.GeneratePdfAsync(_seededClientId, UserId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            UserId,
            "ConsentFormPdfGenerated",
            "Client",
            _seededClientId.ToString(),
            Arg.Any<string>());
    }

    [Fact]
    public async Task GeneratePdfAsync_WithMissingClient_ThrowsInvalidOperationException()
    {
        // Arrange
        const int nonExistentId = 999_801;

        // Act
        var act = () => _sut.GeneratePdfAsync(nonExistentId, UserId);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{nonExistentId}*");
    }

    // ---------------------------------------------------------------------------
    // GenerateDocxAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GenerateDocxAsync_WithValidClient_ReturnsNonEmptyByteArray()
    {
        // Act
        var result = await _sut.GenerateDocxAsync(_seededClientId, UserId);

        // Assert
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GenerateDocxAsync_WithValidClient_CallsTemplateGenerate()
    {
        // Act
        await _sut.GenerateDocxAsync(_seededClientId, UserId);

        // Assert
        _template.Received(1).Generate(
            "Alice FormClient",
            "Jane Smith",
            Arg.Any<DateTime>());
    }

    [Fact]
    public async Task GenerateDocxAsync_WithValidClient_CreatesConsentFormRecord()
    {
        // Act
        await _sut.GenerateDocxAsync(_seededClientId, UserId);

        // Assert
        var forms = await _dbContext.Set<ConsentForm>()
            .Where(f => f.ClientId == _seededClientId)
            .ToListAsync();

        forms.Should().ContainSingle();
    }

    [Fact]
    public async Task GenerateDocxAsync_WithValidClient_CallsAuditLog()
    {
        // Act
        await _sut.GenerateDocxAsync(_seededClientId, UserId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            UserId,
            "ConsentFormDocxGenerated",
            "Client",
            _seededClientId.ToString(),
            Arg.Any<string>());
    }

    [Fact]
    public async Task GenerateDocxAsync_WithMissingClient_ThrowsInvalidOperationException()
    {
        // Arrange
        const int nonExistentId = 999_802;

        // Act
        var act = () => _sut.GenerateDocxAsync(nonExistentId, UserId);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{nonExistentId}*");
    }

    // ---------------------------------------------------------------------------
    // GeneratePreviewPdf tests
    // ---------------------------------------------------------------------------

    [Fact]
    public void GeneratePreviewPdf_WithNames_ReturnsNonEmptyByteArray()
    {
        // Act
        var result = _sut.GeneratePreviewPdf("Preview Client", "Preview Practitioner");

        // Assert
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GeneratePreviewPdf_WithNames_CallsTemplateGenerateWithProvidedNames()
    {
        // Act
        _sut.GeneratePreviewPdf("Preview Client", "Preview Practitioner");

        // Assert
        _template.Received(1).Generate(
            "Preview Client",
            "Preview Practitioner",
            Arg.Any<DateTime>());
    }

    [Fact]
    public void GeneratePreviewPdf_DoesNotAccessDatabase()
    {
        // Act — no client exists in DB, but preview should not query for one
        var act = () => _sut.GeneratePreviewPdf("Any Client", "Any Practitioner");

        // Assert — must not throw despite no client in DB
        act.Should().NotThrow();
    }

    // ---------------------------------------------------------------------------
    // GeneratePreviewDocx tests
    // ---------------------------------------------------------------------------

    [Fact]
    public void GeneratePreviewDocx_WithNames_ReturnsNonEmptyByteArray()
    {
        // Act
        var result = _sut.GeneratePreviewDocx("Preview Client", "Preview Practitioner");

        // Assert
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GeneratePreviewDocx_WithNames_CallsTemplateGenerateWithProvidedNames()
    {
        // Act
        _sut.GeneratePreviewDocx("Preview Client", "Preview Practitioner");

        // Assert
        _template.Received(1).Generate(
            "Preview Client",
            "Preview Practitioner",
            Arg.Any<DateTime>());
    }

    [Fact]
    public void GeneratePreviewDocx_DoesNotAccessDatabase()
    {
        // Act
        var act = () => _sut.GeneratePreviewDocx("Any Client", "Any Practitioner");

        // Assert
        act.Should().NotThrow();
    }

    // ---------------------------------------------------------------------------
    // RecordDigitalSignatureAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task RecordDigitalSignatureAsync_WithValidClient_ReturnsDto()
    {
        // Act
        var result = await _sut.RecordDigitalSignatureAsync(_seededClientId, UserId);

        // Assert
        result.Should().NotBeNull();
        result.ClientId.Should().Be(_seededClientId);
    }

    [Fact]
    public async Task RecordDigitalSignatureAsync_WithValidClient_SetsIsSignedTrue()
    {
        // Act
        await _sut.RecordDigitalSignatureAsync(_seededClientId, UserId);

        // Assert
        var form = await _dbContext.Set<ConsentForm>()
            .FirstAsync(f => f.ClientId == _seededClientId);

        form.IsSigned.Should().BeTrue();
    }

    [Fact]
    public async Task RecordDigitalSignatureAsync_WithValidClient_SetsSignedAt()
    {
        // Arrange
        var before = DateTime.UtcNow.AddSeconds(-1);

        // Act
        await _sut.RecordDigitalSignatureAsync(_seededClientId, UserId);

        // Assert
        var form = await _dbContext.Set<ConsentForm>()
            .FirstAsync(f => f.ClientId == _seededClientId);

        form.SignedAt.Should().NotBeNull();
        form.SignedAt.Should().BeAfter(before);
    }

    [Fact]
    public async Task RecordDigitalSignatureAsync_WithValidClient_SetsSignedByUserId()
    {
        // Act
        await _sut.RecordDigitalSignatureAsync(_seededClientId, UserId);

        // Assert
        var form = await _dbContext.Set<ConsentForm>()
            .FirstAsync(f => f.ClientId == _seededClientId);

        form.SignedByUserId.Should().Be(UserId);
    }

    [Fact]
    public async Task RecordDigitalSignatureAsync_WithValidClient_SetsSignatureMethodToDigital()
    {
        // Act
        await _sut.RecordDigitalSignatureAsync(_seededClientId, UserId);

        // Assert
        var form = await _dbContext.Set<ConsentForm>()
            .FirstAsync(f => f.ClientId == _seededClientId);

        form.SignatureMethod.Should().Be(ConsentSignatureMethod.Digital);
    }

    [Fact]
    public async Task RecordDigitalSignatureAsync_WithValidClient_StoresTemplateVersion()
    {
        // Act
        await _sut.RecordDigitalSignatureAsync(_seededClientId, UserId);

        // Assert
        var form = await _dbContext.Set<ConsentForm>()
            .FirstAsync(f => f.ClientId == _seededClientId);

        form.FormVersion.Should().Be(TemplateVersion);
    }

    [Fact]
    public async Task RecordDigitalSignatureAsync_WithValidClient_CallsGrantConsentAsync()
    {
        // Act
        await _sut.RecordDigitalSignatureAsync(_seededClientId, UserId);

        // Assert
        await _consentService.Received(1).GrantConsentAsync(
            _seededClientId,
            "Treatment and care",
            TemplateVersion,
            UserId);
    }

    [Fact]
    public async Task RecordDigitalSignatureAsync_WithValidClient_CallsAuditLog()
    {
        // Act
        await _sut.RecordDigitalSignatureAsync(_seededClientId, UserId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            UserId,
            "ConsentFormDigitallySigned",
            "Client",
            _seededClientId.ToString(),
            Arg.Any<string>());
    }

    [Fact]
    public async Task RecordDigitalSignatureAsync_WhenUnsignedDigitalFormExists_ReusesThatForm()
    {
        // Arrange — seed an unsigned digital form so GetOrCreateFormAsync finds it
        var existing = SeedConsentForm(method: ConsentSignatureMethod.Digital, isSigned: false);

        // Act
        var result = await _sut.RecordDigitalSignatureAsync(_seededClientId, UserId);

        // Assert — the returned DTO should reference the pre-existing form's Id
        result.Id.Should().Be(existing.Id,
            because: "GetOrCreateFormAsync should reuse an existing unsigned digital form");

        var totalForms = await _dbContext.Set<ConsentForm>()
            .Where(f => f.ClientId == _seededClientId)
            .CountAsync();

        totalForms.Should().Be(1, because: "no new form should be created when an unsigned one already exists");
    }

    [Fact]
    public async Task RecordDigitalSignatureAsync_WhenAlreadySignedDigitalFormExists_CreatesNewForm()
    {
        // Arrange — a previously signed digital form; service must create a fresh one
        SeedConsentForm(method: ConsentSignatureMethod.Digital, isSigned: true);

        // Act
        await _sut.RecordDigitalSignatureAsync(_seededClientId, UserId);

        // Assert — two forms total: one old signed, one newly created and signed
        var forms = await _dbContext.Set<ConsentForm>()
            .Where(f => f.ClientId == _seededClientId)
            .ToListAsync();

        forms.Should().HaveCount(2);
        forms.Should().AllSatisfy(f => f.IsSigned.Should().BeTrue());
    }

    [Fact]
    public async Task RecordDigitalSignatureAsync_ReturnsDtoWithCorrectFields()
    {
        // Act
        var result = await _sut.RecordDigitalSignatureAsync(_seededClientId, UserId);

        // Assert
        result.IsSigned.Should().BeTrue();
        result.SignedByUserId.Should().Be(UserId);
        result.SignatureMethod.Should().Be(ConsentSignatureMethod.Digital);
        result.FormVersion.Should().Be(TemplateVersion);
        result.GeneratedByUserId.Should().Be(UserId);
    }

    // ---------------------------------------------------------------------------
    // MarkPhysicallySignedAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task MarkPhysicallySignedAsync_WithValidClient_ReturnsDto()
    {
        // Act
        var result = await _sut.MarkPhysicallySignedAsync(_seededClientId, UserId);

        // Assert
        result.Should().NotBeNull();
        result.ClientId.Should().Be(_seededClientId);
    }

    [Fact]
    public async Task MarkPhysicallySignedAsync_WithValidClient_SetsIsSignedTrue()
    {
        // Act
        await _sut.MarkPhysicallySignedAsync(_seededClientId, UserId);

        // Assert
        var form = await _dbContext.Set<ConsentForm>()
            .FirstAsync(f => f.ClientId == _seededClientId);

        form.IsSigned.Should().BeTrue();
    }

    [Fact]
    public async Task MarkPhysicallySignedAsync_WithValidClient_SetsSignatureMethodToPhysical()
    {
        // Act
        await _sut.MarkPhysicallySignedAsync(_seededClientId, UserId);

        // Assert
        var form = await _dbContext.Set<ConsentForm>()
            .FirstAsync(f => f.ClientId == _seededClientId);

        form.SignatureMethod.Should().Be(ConsentSignatureMethod.Physical);
    }

    [Fact]
    public async Task MarkPhysicallySignedAsync_WithNotes_PersistsNotes()
    {
        // Arrange
        const string notes = "Client signed at clinic visit on 2026-03-01";

        // Act
        await _sut.MarkPhysicallySignedAsync(_seededClientId, UserId, notes);

        // Assert
        var form = await _dbContext.Set<ConsentForm>()
            .FirstAsync(f => f.ClientId == _seededClientId);

        form.Notes.Should().Be(notes);
    }

    [Fact]
    public async Task MarkPhysicallySignedAsync_WithoutNotes_LeavesNotesNull()
    {
        // Act — notes omitted
        await _sut.MarkPhysicallySignedAsync(_seededClientId, UserId);

        // Assert
        var form = await _dbContext.Set<ConsentForm>()
            .FirstAsync(f => f.ClientId == _seededClientId);

        form.Notes.Should().BeNull();
    }

    [Fact]
    public async Task MarkPhysicallySignedAsync_WithValidClient_SetsSignedByUserId()
    {
        // Act
        await _sut.MarkPhysicallySignedAsync(_seededClientId, UserId);

        // Assert
        var form = await _dbContext.Set<ConsentForm>()
            .FirstAsync(f => f.ClientId == _seededClientId);

        form.SignedByUserId.Should().Be(UserId);
    }

    [Fact]
    public async Task MarkPhysicallySignedAsync_WithValidClient_SetsSignedAt()
    {
        // Arrange
        var before = DateTime.UtcNow.AddSeconds(-1);

        // Act
        await _sut.MarkPhysicallySignedAsync(_seededClientId, UserId);

        // Assert
        var form = await _dbContext.Set<ConsentForm>()
            .FirstAsync(f => f.ClientId == _seededClientId);

        form.SignedAt.Should().NotBeNull();
        form.SignedAt.Should().BeAfter(before);
    }

    [Fact]
    public async Task MarkPhysicallySignedAsync_WithValidClient_CallsGrantConsentAsync()
    {
        // Act
        await _sut.MarkPhysicallySignedAsync(_seededClientId, UserId);

        // Assert
        await _consentService.Received(1).GrantConsentAsync(
            _seededClientId,
            "Treatment and care",
            TemplateVersion,
            UserId);
    }

    [Fact]
    public async Task MarkPhysicallySignedAsync_WithValidClient_CallsAuditLog()
    {
        // Act
        await _sut.MarkPhysicallySignedAsync(_seededClientId, UserId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            UserId,
            "ConsentFormPhysicallySigned",
            "Client",
            _seededClientId.ToString(),
            Arg.Any<string>());
    }

    [Fact]
    public async Task MarkPhysicallySignedAsync_WithValidClient_DispatchesUpdatedNotification()
    {
        // Act
        await _sut.MarkPhysicallySignedAsync(_seededClientId, UserId);

        // Assert
        await _notificationDispatcher.Received().DispatchAsync(Arg.Is<EntityChangeNotification>(n =>
            n.EntityType == "ConsentForm" &&
            n.ChangeType == EntityChangeType.Updated &&
            n.ClientId == _seededClientId));
    }

    [Fact]
    public async Task MarkPhysicallySignedAsync_WhenExistingUnsignedPhysicalFormExists_ReusesThatForm()
    {
        // Arrange — seed an unsigned physical form
        var existing = SeedConsentForm(method: ConsentSignatureMethod.Physical, isSigned: false);

        // Act
        var result = await _sut.MarkPhysicallySignedAsync(_seededClientId, UserId);

        // Assert — service picks up the existing unsigned physical form
        result.Id.Should().Be(existing.Id,
            because: "MarkPhysicallySignedAsync queries for an existing physical form first");
    }

    [Fact]
    public async Task MarkPhysicallySignedAsync_ReturnsDtoWithNotesPopulated()
    {
        // Arrange
        const string notes = "In-person signature obtained";

        // Act
        var result = await _sut.MarkPhysicallySignedAsync(_seededClientId, UserId, notes);

        // Assert
        result.Notes.Should().Be(notes);
        result.IsSigned.Should().BeTrue();
        result.SignatureMethod.Should().Be(ConsentSignatureMethod.Physical);
    }

    // ---------------------------------------------------------------------------
    // UploadScannedCopyAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task UploadScannedCopyAsync_WhenNoFormExists_ReturnsNull()
    {
        // Arrange — no ConsentForm record in DB
        using var stream = new MemoryStream("scan content"u8.ToArray());

        // Act
        var result = await _sut.UploadScannedCopyAsync(_seededClientId, stream, "scan.pdf", UserId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task UploadScannedCopyAsync_WhenFormExists_ReturnsDto()
    {
        // Arrange
        SeedConsentForm();
        using var stream = new MemoryStream("scan content"u8.ToArray());

        // Act
        var result = await _sut.UploadScannedCopyAsync(_seededClientId, stream, "scan.pdf", UserId);

        // Assert
        result.Should().NotBeNull();
        result!.ClientId.Should().Be(_seededClientId);
    }

    [Fact]
    public async Task UploadScannedCopyAsync_WhenFormExists_WritesFileToStoragePath()
    {
        // Arrange
        SeedConsentForm();
        var fileContent = "scan content"u8.ToArray();
        using var stream = new MemoryStream(fileContent);

        // Act
        await _sut.UploadScannedCopyAsync(_seededClientId, stream, "scan.pdf", UserId);

        // Assert — the storage directory should now contain exactly one file
        Directory.Exists(_tempStoragePath).Should().BeTrue();
        var files = Directory.GetFiles(_tempStoragePath);
        files.Should().ContainSingle();
    }

    [Fact]
    public async Task UploadScannedCopyAsync_WhenFormExists_WritesCorrectFileContent()
    {
        // Arrange
        SeedConsentForm();
        var fileContent = "scan content"u8.ToArray();
        using var stream = new MemoryStream(fileContent);

        // Act
        await _sut.UploadScannedCopyAsync(_seededClientId, stream, "scan.pdf", UserId);

        // Assert — the written file should contain what was in the stream
        var files = Directory.GetFiles(_tempStoragePath);
        var writtenBytes = await File.ReadAllBytesAsync(files[0]);
        writtenBytes.Should().BeEquivalentTo(fileContent);
    }

    [Fact]
    public async Task UploadScannedCopyAsync_WhenFormExists_PersistsScannedCopyPath()
    {
        // Arrange
        SeedConsentForm();
        using var stream = new MemoryStream("scan content"u8.ToArray());

        // Act
        var result = await _sut.UploadScannedCopyAsync(_seededClientId, stream, "scan.pdf", UserId);

        // Assert
        result!.ScannedCopyPath.Should().NotBeNullOrEmpty();
        result.ScannedCopyPath.Should().StartWith(_tempStoragePath);
    }

    [Fact]
    public async Task UploadScannedCopyAsync_WhenFormExists_UpdatesFormScannedCopyPathInDb()
    {
        // Arrange
        SeedConsentForm();
        using var stream = new MemoryStream("scan content"u8.ToArray());

        // Act
        await _sut.UploadScannedCopyAsync(_seededClientId, stream, "scan.pdf", UserId);

        // Assert
        var form = await _dbContext.Set<ConsentForm>()
            .FirstAsync(f => f.ClientId == _seededClientId);

        form.ScannedCopyPath.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task UploadScannedCopyAsync_WhenFormExists_SafeFileNameContainsClientId()
    {
        // Arrange
        SeedConsentForm();
        using var stream = new MemoryStream("scan content"u8.ToArray());

        // Act
        var result = await _sut.UploadScannedCopyAsync(_seededClientId, stream, "myscan.pdf", UserId);

        // Assert — the persisted path should embed the clientId for traceability
        result!.ScannedCopyPath.Should().Contain(_seededClientId.ToString());
    }

    [Fact]
    public async Task UploadScannedCopyAsync_WhenFormExists_SafeFileNameContainsOriginalFileName()
    {
        // Arrange
        SeedConsentForm();
        using var stream = new MemoryStream("scan content"u8.ToArray());

        // Act
        var result = await _sut.UploadScannedCopyAsync(_seededClientId, stream, "myscan.pdf", UserId);

        // Assert
        result!.ScannedCopyPath.Should().Contain("myscan.pdf");
    }

    [Fact]
    public async Task UploadScannedCopyAsync_WhenFormExists_CallsAuditLog()
    {
        // Arrange
        SeedConsentForm();
        using var stream = new MemoryStream("scan content"u8.ToArray());

        // Act
        await _sut.UploadScannedCopyAsync(_seededClientId, stream, "scan.pdf", UserId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            UserId,
            "ConsentFormScanUploaded",
            "Client",
            _seededClientId.ToString(),
            Arg.Any<string>());
    }

    [Fact]
    public async Task UploadScannedCopyAsync_WhenFormExists_DispatchesUpdatedNotification()
    {
        // Arrange
        SeedConsentForm();
        _notificationDispatcher.ClearReceivedCalls();
        using var stream = new MemoryStream("scan content"u8.ToArray());

        // Act
        await _sut.UploadScannedCopyAsync(_seededClientId, stream, "scan.pdf", UserId);

        // Assert
        await _notificationDispatcher.Received(1).DispatchAsync(Arg.Is<EntityChangeNotification>(n =>
            n.EntityType == "ConsentForm" &&
            n.ChangeType == EntityChangeType.Updated &&
            n.ClientId == _seededClientId));
    }

    [Fact]
    public async Task UploadScannedCopyAsync_WhenMultipleFormsExist_UpdatesLatestForm()
    {
        // Arrange — two forms; the newer one should be updated
        var olderForm = SeedConsentForm(generatedAt: DateTime.UtcNow.AddDays(-2));
        var newerForm = SeedConsentForm(generatedAt: DateTime.UtcNow.AddDays(-1));
        using var stream = new MemoryStream("scan content"u8.ToArray());

        // Act
        var result = await _sut.UploadScannedCopyAsync(_seededClientId, stream, "scan.pdf", UserId);

        // Assert — returned DTO should correspond to the newer form
        result!.Id.Should().Be(newerForm.Id,
            because: "UploadScannedCopyAsync orders by GeneratedAt descending and takes the first");
    }

    // ---------------------------------------------------------------------------
    // GetLatestFormAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetLatestFormAsync_WhenNoFormsExist_ReturnsNull()
    {
        // Act
        var result = await _sut.GetLatestFormAsync(_seededClientId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetLatestFormAsync_WhenOneFormExists_ReturnsThatForm()
    {
        // Arrange
        var form = SeedConsentForm();

        // Act
        var result = await _sut.GetLatestFormAsync(_seededClientId);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(form.Id);
    }

    [Fact]
    public async Task GetLatestFormAsync_WhenMultipleFormsExist_ReturnsMostRecentByGeneratedAt()
    {
        // Arrange — seed two forms with different generated timestamps
        SeedConsentForm(generatedAt: DateTime.UtcNow.AddDays(-5));
        var newerForm = SeedConsentForm(generatedAt: DateTime.UtcNow.AddDays(-1));

        // Act
        var result = await _sut.GetLatestFormAsync(_seededClientId);

        // Assert
        result!.Id.Should().Be(newerForm.Id,
            because: "GetLatestFormAsync should return the most recently generated form");
    }

    [Fact]
    public async Task GetLatestFormAsync_ReturnsCorrectClientId()
    {
        // Arrange
        SeedConsentForm();

        // Act
        var result = await _sut.GetLatestFormAsync(_seededClientId);

        // Assert
        result!.ClientId.Should().Be(_seededClientId);
    }

    [Fact]
    public async Task GetLatestFormAsync_ForDifferentClient_ReturnsNull()
    {
        // Arrange — form exists for _seededClientId but not for a different client
        SeedConsentForm();

        // Act
        var result = await _sut.GetLatestFormAsync(999_901);

        // Assert
        result.Should().BeNull();
    }

    // ---------------------------------------------------------------------------
    // GetFormsForClientAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetFormsForClientAsync_WhenNoFormsExist_ReturnsEmptyList()
    {
        // Act
        var result = await _sut.GetFormsForClientAsync(_seededClientId);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetFormsForClientAsync_WhenOneFormExists_ReturnsListWithOneItem()
    {
        // Arrange
        SeedConsentForm();

        // Act
        var result = await _sut.GetFormsForClientAsync(_seededClientId);

        // Assert
        result.Should().ContainSingle();
    }

    [Fact]
    public async Task GetFormsForClientAsync_WhenMultipleFormsExist_ReturnsAllFormsOrderedByGeneratedAtDescending()
    {
        // Arrange
        var older = SeedConsentForm(generatedAt: DateTime.UtcNow.AddDays(-10));
        var middle = SeedConsentForm(generatedAt: DateTime.UtcNow.AddDays(-5));
        var newer = SeedConsentForm(generatedAt: DateTime.UtcNow.AddDays(-1));

        // Act
        var result = await _sut.GetFormsForClientAsync(_seededClientId);

        // Assert
        result.Should().HaveCount(3);
        result[0].Id.Should().Be(newer.Id, because: "most recent form should be first");
        result[1].Id.Should().Be(middle.Id);
        result[2].Id.Should().Be(older.Id, because: "oldest form should be last");
    }

    [Fact]
    public async Task GetFormsForClientAsync_OnlyReturnsFormsForRequestedClient()
    {
        // Arrange — forms for seeded client and a noise form for a different real client
        SeedConsentForm();
        _dbContext.Set<ConsentForm>().Add(new ConsentForm
        {
            ClientId = _otherClientId,
            FormVersion = TemplateVersion,
            GeneratedAt = DateTime.UtcNow,
            GeneratedByUserId = UserId,
            SignatureMethod = ConsentSignatureMethod.Physical,
            IsSigned = false
        });
        _dbContext.SaveChanges();

        // Act
        var result = await _sut.GetFormsForClientAsync(_seededClientId);

        // Assert
        result.Should().ContainSingle(because: "only forms belonging to the requested client should be returned");
        result[0].ClientId.Should().Be(_seededClientId);
    }

    [Fact]
    public async Task GetFormsForClientAsync_ReturnsDtosWithCorrectFields()
    {
        // Arrange
        var form = SeedConsentForm(method: ConsentSignatureMethod.Digital, isSigned: false);

        // Act
        var result = await _sut.GetFormsForClientAsync(_seededClientId);

        // Assert
        var dto = result.Single();
        dto.Id.Should().Be(form.Id);
        dto.ClientId.Should().Be(_seededClientId);
        dto.FormVersion.Should().Be(TemplateVersion);
        dto.SignatureMethod.Should().Be(ConsentSignatureMethod.Digital);
        dto.IsSigned.Should().BeFalse();
        dto.GeneratedByUserId.Should().Be(UserId);
    }

    // ---------------------------------------------------------------------------
    // Cleanup
    // ---------------------------------------------------------------------------

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();

        if (Directory.Exists(_tempStoragePath))
        {
            Directory.Delete(_tempStoragePath, recursive: true);
        }
    }
}
