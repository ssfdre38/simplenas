using Microsoft.AspNetCore.SignalR;
using System.Text.Json;

namespace SimpleNAS;

/// <summary>
/// Real-time SignalR Hub for streaming system metrics, SMART telemetry, container states, and audit notifications.
/// </summary>
public class NasHub : Hub
{
    private readonly ILogger<NasHub> _logger;

    public NasHub(ILogger<NasHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("SignalR Client connected: {ConnectionId}", Context.ConnectionId);
        await Groups.AddToGroupAsync(Context.ConnectionId, "TelemetrySubscribers");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("SignalR Client disconnected: {ConnectionId}", Context.ConnectionId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "TelemetrySubscribers");
        await base.OnDisconnectedAsync(exception);
    }

    public async Task SendClientAlert(string title, string message, string level = "info")
    {
        await Clients.All.SendAsync("ReceiveAlert", new
        {
            title,
            message,
            level,
            timestamp = DateTime.UtcNow.ToString("o")
        });
    }
}

/// <summary>
/// Background worker pushing high-fidelity telemetry over SignalR every 1.5 seconds.
/// </summary>
public class TelemetryStreamingService : BackgroundService
{
    private readonly IHubContext<NasHub> _hubContext;
    private readonly ILogger<TelemetryStreamingService> _logger;

    public TelemetryStreamingService(IHubContext<NasHub> hubContext, ILogger<TelemetryStreamingService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TelemetryStreamingService started (1.5s interval).");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var telemetry = SystemTelemetry.GetLiveTelemetry();
                var smartData = DiskHealthService.GetCachedDiskHealth();

                await _hubContext.Clients.Group("TelemetrySubscribers").SendAsync("ReceiveTelemetry", new
                {
                    cpu = telemetry.Cpu,
                    memory = telemetry.Memory,
                    disk = telemetry.Disk,
                    platform = telemetry.Platform,
                    timestamp = DateTime.UtcNow.ToString("HH:mm:ss"),
                    smart = smartData
                }, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Error broadcasting telemetry: {Message}", ex.Message);
            }

            await Task.Delay(1500, stoppingToken);
        }
    }
}
