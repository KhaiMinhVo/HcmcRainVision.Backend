namespace HcmcRainVision.Backend.Services.Chatbot
{
    public interface ILlmIntentService
    {
        Task<LlmIntentResult?> ParseIntentAsync(string userMessage, CancellationToken cancellationToken = default);
    }
}
