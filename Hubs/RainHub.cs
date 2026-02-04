using Microsoft.AspNetCore.SignalR;
using HcmcRainVision.Backend.Models.Constants;
using HcmcRainVision.Backend.Utils;

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
        /// Client có thể gửi "Quận 1", Server tự chuẩn hóa thành "quan_1"
        /// </summary>
        public async Task JoinDistrictGroup(string districtName)
        {
            var normalizedName = StringUtils.NormalizeCode(districtName);
            await Groups.AddToGroupAsync(Context.ConnectionId, normalizedName);
            _logger.LogInformation($"Client {Context.ConnectionId} joined district group: {normalizedName} (from: {districtName})");
        }

        /// <summary>
        /// Client rời khỏi nhóm District
        /// </summary>
        public async Task LeaveDistrictGroup(string districtName)
        {
            var normalizedName = StringUtils.NormalizeCode(districtName);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, normalizedName);
            _logger.LogInformation($"Client {Context.ConnectionId} left district group: {normalizedName}");
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
