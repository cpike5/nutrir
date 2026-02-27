using Nutrir.Core.DTOs;

namespace Nutrir.Web.Services;

public class RealTimeNotificationService : IAsyncDisposable
{
    private readonly NotificationBroadcaster _broadcaster;
    private readonly ILogger<RealTimeNotificationService> _logger;
    private bool _subscribed;

    public event Action<EntityChangeNotification>? OnEntityChanged;

    public RealTimeNotificationService(
        NotificationBroadcaster broadcaster,
        ILogger<RealTimeNotificationService> logger)
    {
        _broadcaster = broadcaster;
        _logger = logger;
    }

    public Task StartAsync()
    {
        if (_subscribed)
            return Task.CompletedTask;

        _broadcaster.OnBroadcast += HandleBroadcast;
        _subscribed = true;
        _logger.LogDebug("Subscribed to in-process notification broadcaster");

        return Task.CompletedTask;
    }

    private void HandleBroadcast(EntityChangeNotification notification)
    {
        OnEntityChanged?.Invoke(notification);
    }

    public ValueTask DisposeAsync()
    {
        if (_subscribed)
        {
            _broadcaster.OnBroadcast -= HandleBroadcast;
            _subscribed = false;
        }

        return ValueTask.CompletedTask;
    }
}
