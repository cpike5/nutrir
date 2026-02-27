using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Nutrir.Core.DTOs;

namespace Nutrir.Web.Services;

public class RealTimeNotificationService : IAsyncDisposable
{
    private readonly NavigationManager _navigationManager;
    private readonly ILogger<RealTimeNotificationService> _logger;
    private readonly string? _authCookie;
    private HubConnection? _connection;

    public event Action<EntityChangeNotification>? OnEntityChanged;

    public RealTimeNotificationService(
        NavigationManager navigationManager,
        IHttpContextAccessor httpContextAccessor,
        ILogger<RealTimeNotificationService> logger)
    {
        _navigationManager = navigationManager;
        _logger = logger;

        // Capture auth cookie during construction (HttpContext is only available during the initial HTTP request)
        _authCookie = httpContextAccessor.HttpContext?.Request.Cookies[".AspNetCore.Identity.Application"];
    }

    public async Task StartAsync()
    {
        if (_connection is not null)
            return;

        if (string.IsNullOrEmpty(_authCookie))
        {
            _logger.LogDebug("No auth cookie available — real-time notifications disabled for this circuit");
            return;
        }

        try
        {
            var hubUrl = _navigationManager.ToAbsoluteUri("/hubs/nutrir");

            _connection = new HubConnectionBuilder()
                .WithUrl(hubUrl, opts =>
                {
                    opts.Headers.Add("Cookie", $".AspNetCore.Identity.Application={_authCookie}");
                })
                .WithAutomaticReconnect()
                .Build();

            _connection.On<EntityChangeNotification>("EntityChanged", notification =>
            {
                OnEntityChanged?.Invoke(notification);
            });

            _connection.Reconnecting += ex =>
            {
                _logger.LogWarning(ex, "SignalR reconnecting");
                return Task.CompletedTask;
            };

            _connection.Reconnected += connectionId =>
            {
                _logger.LogInformation("SignalR reconnected with connection {ConnectionId}", connectionId);
                return Task.CompletedTask;
            };

            await _connection.StartAsync();
            _logger.LogDebug("SignalR connection started");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to start SignalR connection — real-time notifications unavailable");
            _connection = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
    }
}
