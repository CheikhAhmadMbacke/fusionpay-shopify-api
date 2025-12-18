using FusionPayProxy.Models.Requests;
using FusionPayProxy.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FusionPayProxy.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WebhookController : ControllerBase
    {
        private readonly IFusionPayService _fusionPayService;
        private readonly ShopifyService _shopifyService;
        private readonly ILogger<WebhookController> _logger;

        public WebhookController(
            IFusionPayService fusionPayService,
            ShopifyService shopifyService,
            ILogger<WebhookController> logger)
        {
            _fusionPayService = fusionPayService;
            _shopifyService = shopifyService;
            _logger = logger;
        }

        [HttpPost("fusionpay")]
        public async Task<IActionResult> HandleFusionPayWebhook()
        {
            try
            {
                _logger.LogInformation("📨 Received FusionPay webhook request");

                // Lire le JSON
                var json = await new StreamReader(Request.Body).ReadToEndAsync();
                _logger.LogDebug("📥 Webhook payload: {Json}", json);

                // Désérialiser avec options flexibles
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    NumberHandling = JsonNumberHandling.AllowReadingFromString // Important !
                };

                var webhookData = JsonSerializer.Deserialize<FusionPayWebhookRequest>(json, options);

                if (webhookData == null)
                {
                    _logger.LogError("❌ Failed to deserialize webhook");
                    return BadRequest("Invalid webhook data");
                }

                // Gérer le token (string ou number)
                string tokenPay = webhookData.TokenPay;

                // Appeler le service
                var result = await _fusionPayService.HandleWebhookAsync(webhookData);

                return result ? Ok() : StatusCode(500);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Error processing webhook");
                return StatusCode(500);
            }
        }

        [HttpGet("test")]
        public IActionResult TestWebhook()
        {
            return Ok(new
            {
                message = "Webhook endpoint is working",
                timestamp = DateTime.UtcNow,
                url = "/api/webhook/fusionpay"
            });
        }
    }
}
