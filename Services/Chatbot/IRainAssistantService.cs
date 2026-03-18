using HcmcRainVision.Backend.Models.DTOs;

namespace HcmcRainVision.Backend.Services.Chatbot
{
    public interface IRainAssistantService
    {
        Task<DistrictRainResult?> GetDistrictRainAsync(string districtName, CancellationToken cancellationToken = default);
        Task<RouteRainResult> AnalyzeRouteRainAsync(List<RoutePointDto> routePoints, CancellationToken cancellationToken = default);
    }
}
