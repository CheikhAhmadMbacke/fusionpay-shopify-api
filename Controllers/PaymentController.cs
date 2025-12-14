using FusionPayProxy.Models.Requests;
using FusionPayProxy.Services;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace FusionPayProxy.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [EnableCors("ShopifyPolicy")]
    public class PaymentController : ControllerBase
    {
        private readonly IFusionPayService _fusionPayService;
        private readonly ShopifyService _shopifyService;
        private readonly ILogger<PaymentController> _logger;
        private readonly IConfiguration _configuration;

        public PaymentController(
            IFusionPayService fusionPayService,
            ShopifyService shopifyService,
            ILogger<PaymentController> logger,
            IConfiguration configuration)
        {
            _fusionPayService = fusionPayService;
            _shopifyService = shopifyService;
            _logger = logger;
            _configuration = configuration;
        }

        [HttpPost("initiate")]
        public async Task<IActionResult> InitiatePayment([FromBody] PaymentRequest request)
        {
            try
            {
                _logger.LogInformation("🚀 Received payment initiation request for order {OrderId}", request.OrderId);

                // Validation CORS
                if (!IsValidOrigin(Request))
                {
                    _logger.LogWarning("🛑 Blocked request from invalid origin: {Origin}",
                        Request.Headers.Origin);
                    return Unauthorized(new { error = "Invalid origin" });
                }

                // Validation des données
                var validationResult = ValidatePaymentRequest(request);
                if (!validationResult.IsValid)
                {
                    _logger.LogWarning("🛑 Invalid payment request: {Errors}",
                        string.Join(", ", validationResult.Errors));
                    return BadRequest(new { errors = validationResult.Errors });
                }

                // Créer un enregistrement Shopify
                await _shopifyService.CreateShopifyOrderRecordAsync(
                    request.OrderId,
                    request.OrderNumber,
                    request.Amount
                );

                // Initier le paiement FusionPay
                var result = await _fusionPayService.InitiatePaymentAsync(request);

                if (result.Success)
                {
                    _logger.LogInformation("✅ Payment initiated successfully. Redirecting to: {Url}",
                        result.PaymentUrl);

                    return Ok(new
                    {
                        success = true,
                        paymentUrl = result.PaymentUrl,
                        token = result.Token,
                        transactionId = result.TransactionId,
                        message = result.Message
                    });
                }
                else
                {
                    _logger.LogError("❌ Payment initiation failed: {Error}", result.ErrorMessage);
                    return BadRequest(new
                    {
                        success = false,
                        error = result.ErrorMessage,
                        message = "Échec de l'initiation du paiement"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Unexpected error in payment initiation");
                return StatusCode(500, new
                {
                    success = false,
                    error = "Internal server error",
                    message = ex.Message
                });
            }
        }

        [HttpGet("verify/{token}")]
        public async Task<IActionResult> VerifyPayment(string token)
        {
            try
            {
                _logger.LogDebug("🔍 Verifying payment with token: {Token}", token);

                var status = await _fusionPayService.VerifyPaymentStatusAsync(token);
                var transaction = await _fusionPayService.GetTransactionByTokenAsync(token);

                return Ok(new
                {
                    token = token,
                    status = status,
                    transaction = transaction != null ? new
                    {
                        id = transaction.Id,
                        orderId = transaction.ShopifyOrderId,
                        amount = transaction.Amount,
                        customerPhone = transaction.CustomerPhone,
                        createdAt = transaction.CreatedAt
                    } : null,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Error verifying payment");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("transactions/pending")]
        public async Task<IActionResult> GetPendingTransactions()
        {
            try
            {
                var transactions = await _fusionPayService.GetPendingTransactionsAsync();
                return Ok(new
                {
                    count = transactions.Count,
                    transactions = transactions.Select(t => new
                    {
                        id = t.Id,
                        orderId = t.ShopifyOrderId,
                        token = t.FusionPayToken,
                        amount = t.Amount,
                        status = t.Status,
                        createdAt = t.CreatedAt,
                        customerPhone = t.CustomerPhone
                    }),
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Error getting pending transactions");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("health")]
        public IActionResult HealthCheck()
        {
            return Ok(new
            {
                status = "healthy",
                service = "FusionPay Proxy API",
                version = "1.0.0",
                timestamp = DateTime.UtcNow,
                database = "SQLite",
                features = new[]
                {
                    "FusionPay Integration",
                    "Shopify Webhooks",
                    "Transaction Tracking",
                    "WhatsApp Notifications"
                }
            });
        }

        // ========== MÉTHODES PRIVÉES ==========

        private bool IsValidOrigin(HttpRequest request)
        {
            var allowedOrigins = _configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                ?? new[] { "https://afrokingvap.com", "https://checkout.shopify.com" };

            var origin = request.Headers.Origin.ToString();
            return string.IsNullOrEmpty(origin) || allowedOrigins.Contains(origin);
        }

        private (bool IsValid, List<string> Errors) ValidatePaymentRequest(PaymentRequest request)
        {
            var errors = new List<string>();

            if (request.Amount <= 200)
                errors.Add("Amount must be greater than 200");

            if (string.IsNullOrWhiteSpace(request.CustomerPhone))
                errors.Add("Customer phone is required");
            else if (request.CustomerPhone.Length < 8)
                errors.Add("Phone number must be at least 8 digits");

            if (string.IsNullOrWhiteSpace(request.CustomerName))
                errors.Add("Customer name is required");

            if (string.IsNullOrWhiteSpace(request.OrderId))
                errors.Add("Order ID is required");

            if (string.IsNullOrWhiteSpace(request.ReturnUrl))
                errors.Add("Return URL is required");

            return (!errors.Any(), errors);
        }
    }
}
