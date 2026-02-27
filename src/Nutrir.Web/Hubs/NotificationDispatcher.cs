using Microsoft.AspNetCore.SignalR;
using Nutrir.Core.DTOs;
using Nutrir.Core.Interfaces;

namespace Nutrir.Web.Hubs;

public class NotificationDispatcher : INotificationDispatcher
{
    private readonly IHubContext<NutrirHub> _hubContext;

    public NotificationDispatcher(IHubContext<NutrirHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task DispatchAsync(EntityChangeNotification notification)
    {
        var group = NutrirHub.GetGroupName(notification.PractitionerUserId);
        await _hubContext.Clients.Group(group).SendAsync("EntityChanged", notification);
    }
}
