using Nutrir.Core.Enums;

namespace Nutrir.Core.DTOs;

public record EntityChangeNotification(
    string EntityType,
    int EntityId,
    EntityChangeType ChangeType,
    string PractitionerUserId,
    DateTime Timestamp);
