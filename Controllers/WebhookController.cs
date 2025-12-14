using System.Text.Json;
using FusionPayProxy.Models.Requests;
using FusionPayProxy.Services;
using Microsoft.AspNetCore.Mvc;

namespace FusionPayProxy.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WebhookController : ControllerBase
    {
        private readonly IFusionPayService _fusionPayService;
        private readonly ShopifyService _shopifyService;
        private readonly ILogger<WebhookController> _logger;
        private readonly IConfiguration _configuration;

        public WebhookController(
            IFusionPayService fusionPayService,
            ShopifyService shopifyService,
            ILogger<WebhookController> logger,
            IConfiguration configuration)
        {
            _fusionPayService = fusionPayService;
            _shopifyService = shopifyService;
            _logger = logger;
            _configuration = configuration;
        }

        [HttpPost("fusionpay")]
        public async Task<IActionResult> HandleFusionPayWebhook()
        {
            try
            {
                _logger.LogInformation("📨 Received FusionPay webhook request");

                using var reader = new StreamReader(Request.Body);
                var body = await reader.ReadToEndAsync();

                if (string.IsNullOrEmpty(body))
                {
                    _logger.LogWarning("🛑 Empty webhook body received");
                    return BadRequest(new { error = "Empty body" });
                }

                var jsonElement = JsonSerializer.Deserialize<JsonElement>(body);

                // Extraire les données de base
                var tokenPay = jsonElement.GetProperty("tokenPay").GetString() ?? "";
                var eventType = jsonElement.GetProperty("event").GetString() ?? "";
                var orderId = "";

                // Extraire personal_Info
                if (jsonElement.TryGetProperty("personal_Info", out var personalInfoArray) &&
                    personalInfoArray.GetArrayLength() > 0)
                {
                    var firstItem = personalInfoArray[0];
                    orderId = firstItem.TryGetProperty("orderId", out var orderIdProp)
                        ? orderIdProp.GetString() ?? ""
                        : "";
                }

                _logger.LogInformation("Processing webhook: Event={Event}, OrderId={OrderId}",
                    eventType, orderId);

                // Créer objet webhook
                var webhook = new FusionPayWebhookRequest
                {
                    Event = eventType,
                    TokenPay = tokenPay,
                    NumeroSend = jsonElement.GetProperty("numeroSend").GetString() ?? "",
                    Nomclient = jsonElement.GetProperty("nomclient").GetString() ?? "",
                    Montant = jsonElement.GetProperty("Montant").GetDecimal(),
                    Frais = jsonElement.GetProperty("frais").GetDecimal(),
                    CreatedAt = jsonElement.GetProperty("createdAt").GetDateTime()
                };

                if (!string.IsNullOrEmpty(orderId))
                {
                    webhook.Personal_Info = new List<PersonalInfo>
                    {
                        new PersonalInfo { OrderId = orderId }
                    };
                }

                // Traiter le webhook
                var processed = await _fusionPayService.HandleWebhookAsync(webhook);

                if (processed && eventType == "payin.session.completed" && !string.IsNullOrEmpty(orderId))
                {
                    // 1. Mettre à jour Shopify
                    await _shopifyService.UpdateOrderStatusAsync(orderId, "paid");

                    // 2. Déclencher WhatsApp
                    await TriggerWhatsAppNotificationAsync(orderId);

                    _logger.LogInformation("✅ Order {OrderId} marked as paid", orderId);
                }

                return Ok(new
                {
                    message = "Webhook processed successfully",
                    eventType = eventType,
                    token = tokenPay
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Error processing webhook");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        [HttpGet("test")]
        public IActionResult TestWebhook()
        {
            return Ok(new
            {
                message = "Webhook endpoint is working",
                timestamp = DateTime.UtcNow
            });
        }

        private async Task TriggerWhatsAppNotificationAsync(string orderId)
        {
            try
            {
                var n8nUrl = _configuration["N8n:WebhookUrl"];
                if (string.IsNullOrEmpty(n8nUrl))
                {
                    _logger.LogWarning("⚠️ N8n webhook URL not configured");
                    return;
                }

                var payload = new
                {
                    eventType = "payment_completed",
                    orderId = orderId,
                    timestamp = DateTime.UtcNow
                };

                using var httpClient = new HttpClient();
                var response = await httpClient.PostAsJsonAsync(n8nUrl, payload);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("📱 WhatsApp notification triggered for order {OrderId}", orderId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Error triggering WhatsApp notification");
            }
        }
    }
}
