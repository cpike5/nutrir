using Nutrir.Core.DTOs;

namespace Nutrir.Core.Interfaces;

public interface INotificationDispatcher
{
    Task DispatchAsync(EntityChangeNotification notification);
}
