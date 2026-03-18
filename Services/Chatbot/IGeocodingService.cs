using HcmcRainVision.Backend.Models.DTOs;

namespace HcmcRainVision.Backend.Services.Chatbot
{
    /// <summary>
    /// Service để chuyển hóa tên địa chỉ thành tọa độ (lat/lng)
    /// Ví dụ: "Thủ Đức" → {Lat: 10.8216, Lng: 106.7776}
    /// </summary>
    public interface IGeocodingService
    {
        /// <summary>
        /// Chuyển hóa tên địa chỉ hoặc quận thành tọa độ lat/lng
        /// </summary>
        /// <param name="place">Tên địa chỉ (ví dụ: "Thủ Đức", "Quận 1", "Bến Thành")</param>
        /// <param name="cancellationToken">Token để hủy bỏ request</param>
        /// <returns>RoutePointDto chứa vĩ độ (Lat) và kinh độ (Lng)</returns>
        Task<RoutePointDto> GeocodeAsync(string place, CancellationToken cancellationToken = default);
    }
}
