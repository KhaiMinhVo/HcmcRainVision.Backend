using Microsoft.AspNetCore.SignalR;
using HcmcRainVision.Backend.Models.Constants;

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

        /// <summary>
        /// Client gọi hàm này để tham gia nhóm theo Phường (WardId)
        /// </summary>
        public async Task JoinWardGroup(string wardId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, wardId);
            _logger.LogInformation($"Client {Context.ConnectionId} joined ward group: {wardId}");
        }

        /// <summary>
        /// Client rời khỏi nhóm Phường
        /// </summary>
        public async Task LeaveWardGroup(string wardId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, wardId);
            _logger.LogInformation($"Client {Context.ConnectionId} left ward group: {wardId}");
        }

        /// <summary>
        /// Client gọi hàm này để tham gia nhóm Dashboard (nhận tất cả thông báo)
        /// </summary>
        public async Task JoinDashboard()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, AppConstants.SignalRGroups.Dashboard);
            _logger.LogInformation($"Client {Context.ConnectionId} joined Dashboard group");
        }

        // Client sẽ lắng nghe sự kiện "ReceiveRainAlert" để nhận thông báo mưa
    }
}
