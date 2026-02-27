using Microsoft.AspNetCore.SignalR;
using Nutrir.Core.DTOs;
using Nutrir.Core.Interfaces;
using Nutrir.Web.Services;

namespace Nutrir.Web.Hubs;

public class NotificationDispatcher : INotificationDispatcher
{
    private readonly IHubContext<NutrirHub> _hubContext;
    private readonly NotificationBroadcaster _broadcaster;

    public NotificationDispatcher(IHubContext<NutrirHub> hubContext, NotificationBroadcaster broadcaster)
    {
        _hubContext = hubContext;
        _broadcaster = broadcaster;
    }

    public async Task DispatchAsync(EntityChangeNotification notification)
    {
        // Broadcast in-process to all active Blazor circuits
        _broadcaster.Broadcast(notification);

        // Also send via SignalR for future external clients (mobile apps, etc.)
        var group = NutrirHub.GetGroupName(notification.PractitionerUserId);
        await _hubContext.Clients.Group(group).SendAsync("EntityChanged", notification);
    }
}
