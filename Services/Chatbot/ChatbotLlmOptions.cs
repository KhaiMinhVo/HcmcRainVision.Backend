namespace HcmcRainVision.Backend.Services.Chatbot
{
    public class ChatbotLlmOptions
    {
        public bool Enabled { get; set; }
        public string BaseUrl { get; set; } = "https://api.openai.com/v1";
        public string Model { get; set; } = "gpt-4o-mini";
        public string ApiKey { get; set; } = string.Empty;
        public int TimeoutSeconds { get; set; } = 20;
    }
}
