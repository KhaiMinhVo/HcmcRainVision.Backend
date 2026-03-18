using HcmcRainVision.Backend.Models.DTOs;
using HcmcRainVision.Backend.Services.Chatbot;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace HcmcRainVision.Backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ChatbotController : ControllerBase
    {
        private readonly IChatbotService _chatbotService;

        public ChatbotController(IChatbotService chatbotService)
        {
            _chatbotService = chatbotService;
        }

        [HttpPost("ask")]
        public async Task<IActionResult> Ask([FromBody] ChatbotAskRequest request, CancellationToken cancellationToken)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("id")?.Value;
            if (int.TryParse(userIdClaim, out var userId))
            {
                request.UserId = userId;
            }

            var response = await _chatbotService.AskAsync(request, cancellationToken);
            return Ok(response);
        }
    }
}
