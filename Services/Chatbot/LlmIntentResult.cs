namespace HcmcRainVision.Backend.Services.Chatbot
{
    public class LlmIntentResult
    {
        public string Intent { get; set; } = "unknown";
        public string? District { get; set; }
        public string? Origin { get; set; }
        public string? Destination { get; set; }
        public double Confidence { get; set; }
        public string? RawArguments { get; set; }
    }
}
