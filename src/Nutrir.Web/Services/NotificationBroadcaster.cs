using Nutrir.Core.DTOs;

namespace Nutrir.Web.Services;

/// <summary>
/// Singleton in-memory event bus that bridges NotificationDispatcher to all active
/// RealTimeNotificationService instances (one per Blazor circuit).
/// </summary>
public class NotificationBroadcaster
{
    public event Action<EntityChangeNotification>? OnBroadcast;

    public void Broadcast(EntityChangeNotification notification)
    {
        OnBroadcast?.Invoke(notification);
    }
}
