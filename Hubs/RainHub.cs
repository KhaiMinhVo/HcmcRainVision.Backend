using Microsoft.AspNetCore.SignalR;

namespace HcmcRainVision.Backend.Hubs
{
    /// <summary>
    /// SignalR Hub để gửi thông báo mưa thời gian thực xuống tất cả client
    /// </summary>
    public class RainHub : Hub
    {
        private readonly ILogger<RainHub> _logger;

        public RainHub(ILogger<RainHub> logger)
        {
            _logger = logger;
        }

        public override async Task OnConnectedAsync()
        {
            _logger.LogInformation($"Client connected: {Context.ConnectionId}");
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            _logger.LogInformation($"Client disconnected: {Context.ConnectionId}");
            await base.OnDisconnectedAsync(exception);
        }

        // Client sẽ lắng nghe sự kiện "ReceiveRainAlert" để nhận thông báo mưa
    }
}
