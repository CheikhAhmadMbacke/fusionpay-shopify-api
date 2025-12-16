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

                using var reader = new StreamReader(Request.Body);
                var body = await reader.ReadToEndAsync();

                if (string.IsNullOrEmpty(body))
                {
                    _logger.LogWarning("🛑 Empty webhook body received");
                    return BadRequest(new { error = "Empty body" });
                }

                _logger.LogDebug("📝 Webhook body: {Body}", body);

                var jsonElement = JsonSerializer.Deserialize<JsonElement>(body);

                // ✅ CORRECTION: Vérification des champs
                var tokenPay = jsonElement.TryGetProperty("tokenPay", out var tokenProp)
                    ? tokenProp.GetString() ?? ""
                    : "";

                var eventType = jsonElement.TryGetProperty("event", out var eventProp)
                    ? eventProp.GetString() ?? ""
                    : "";

                var orderId = "";
                var numeroSend = "";
                var nomclient = "";
                decimal montant = 0;
                decimal frais = 0;
                DateTime? createdAt = null;

                // Extraire les champs optionnels
                if (jsonElement.TryGetProperty("numeroSend", out var numeroSendProp))
                    numeroSend = numeroSendProp.GetString() ?? "";

                if (jsonElement.TryGetProperty("nomclient", out var nomclientProp))
                    nomclient = nomclientProp.GetString() ?? "";

                if (jsonElement.TryGetProperty("Montant", out var montantProp))
                    decimal.TryParse(montantProp.GetString(), out montant);

                if (jsonElement.TryGetProperty("frais", out var fraisProp))
                    decimal.TryParse(fraisProp.GetString(), out frais);

                if (jsonElement.TryGetProperty("createdAt", out var createdAtProp))
                {
                    if (createdAtProp.ValueKind == JsonValueKind.String)
                        DateTime.TryParse(createdAtProp.GetString(), out var date);
                }

                // Extraire personal_Info
                if (jsonElement.TryGetProperty("personal_Info", out var personalInfoArray) &&
                    personalInfoArray.GetArrayLength() > 0)
                {
                    var firstItem = personalInfoArray[0];
                    if (firstItem.TryGetProperty("orderId", out var orderIdProp))
                        orderId = orderIdProp.GetString() ?? "";
                }

                _logger.LogInformation("🔍 Processing webhook: Event={Event}, Token={Token}, OrderId={OrderId}",
                    eventType, tokenPay, orderId);

                // Créer objet webhook
                var webhook = new FusionPayWebhookRequest
                {
                    Event = eventType,
                    TokenPay = tokenPay,
                    NumeroSend = numeroSend,
                    Nomclient = nomclient,
                    Montant = montant,
                    Frais = frais,
                    CreatedAt = createdAt ?? DateTime.UtcNow
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
                    // Mettre à jour Shopify
                    await _shopifyService.UpdateOrderStatusAsync(orderId, "paid");
                    _logger.LogInformation("✅ Order {OrderId} marked as paid in Shopify", orderId);

                    // WhatsApp sera géré par n8n via Shopify webhook
                }

                return Ok(new
                {
                    success = true,
                    message = "Webhook processed successfully",
                    eventType = eventType,
                    token = tokenPay,
                    orderId = orderId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Error processing webhook");
                return StatusCode(500, new
                {
                    success = false,
                    error = "Internal server error",
                    message = ex.Message
                });
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
