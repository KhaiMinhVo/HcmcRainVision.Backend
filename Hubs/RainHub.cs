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

        /// <summary>
        /// Client gọi hàm này để tham gia nhóm theo Quận/District
        /// </summary>
        public async Task JoinDistrictGroup(string districtName)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, districtName);
            _logger.LogInformation($"Client {Context.ConnectionId} joined district group: {districtName}");
        }

        /// <summary>
        /// Client gọi hàm này để rời khỏi nhóm Quận/District
        /// </summary>
        public async Task LeaveDistrictGroup(string districtName)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, districtName);
            _logger.LogInformation($"Client {Context.ConnectionId} left district group: {districtName}");
        }

        /// <summary>
        /// Client gọi hàm này để tham gia nhóm Dashboard (nhận tất cả thông báo)
        /// </summary>
        public async Task JoinDashboard()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "Dashboard");
            _logger.LogInformation($"Client {Context.ConnectionId} joined Dashboard group");
        }

        // Client sẽ lắng nghe sự kiện "ReceiveRainAlert" để nhận thông báo mưa
    }
}
