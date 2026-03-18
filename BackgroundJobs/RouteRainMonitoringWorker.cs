using HcmcRainVision.Backend.Hubs;
using HcmcRainVision.Backend.Models.Constants;
using HcmcRainVision.Backend.Services.Chatbot;
using Microsoft.AspNetCore.SignalR;

namespace HcmcRainVision.Backend.BackgroundJobs
{
    public class RouteRainMonitoringWorker : BackgroundService
    {
        private readonly ILogger<RouteRainMonitoringWorker> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly RouteMonitoringRegistry _registry;
        private readonly IHubContext<RainHub> _hubContext;

        public RouteRainMonitoringWorker(
            ILogger<RouteRainMonitoringWorker> logger,
            IServiceProvider serviceProvider,
            RouteMonitoringRegistry registry,
            IHubContext<RainHub> hubContext)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _registry = registry;
            _hubContext = hubContext;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Route rain monitoring worker started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var snapshots = _registry.GetSnapshots();
                    if (snapshots.Count > 0)
                    {
                        using var scope = _serviceProvider.CreateScope();
                        var rainAssistantService = scope.ServiceProvider.GetRequiredService<IRainAssistantService>();

                        foreach (var snapshot in snapshots)
                        {
                            if (snapshot.RoutePoints.Count < 2)
                            {
                                continue;
                            }

                            var routeAnalysis = await rainAssistantService.AnalyzeRouteRainAsync(snapshot.RoutePoints, stoppingToken);
                            var groupName = _registry.GetGroupName(snapshot.RouteId);

                            var payload = new
                            {
                                routeId = snapshot.RouteId,
                                origin = snapshot.Origin,
                                destination = snapshot.Destination,
                                updatedAtUtc = DateTime.UtcNow,
                                analysis = routeAnalysis
                            };

                            await _hubContext.Clients.Group(groupName)
                                .SendAsync(AppConstants.SignalRGroups.ReceiveRouteRainUpdateMethod, payload, stoppingToken);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Route rain monitoring worker iteration failed.");
                }

                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }
}
