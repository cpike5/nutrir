using Nutrir.Core.Enums;

namespace Nutrir.Core.DTOs;

public record ConsentFormDto(
    int Id,
    int ClientId,
    string FormVersion,
    DateTime GeneratedAt,
    string GeneratedByUserId,
    ConsentSignatureMethod SignatureMethod,
    bool IsSigned,
    DateTime? SignedAt,
    string? SignedByUserId,
    string? ScannedCopyPath,
    string? Notes);
