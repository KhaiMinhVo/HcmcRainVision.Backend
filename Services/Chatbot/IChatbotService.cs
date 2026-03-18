using HcmcRainVision.Backend.Models.DTOs;

namespace HcmcRainVision.Backend.Services.Chatbot
{
    public interface IChatbotService
    {
        Task<ChatbotAskResponse> AskAsync(ChatbotAskRequest request, CancellationToken cancellationToken = default);
    }
}
