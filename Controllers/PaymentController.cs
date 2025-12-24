using FusionPayProxy.Models.Requests;
using FusionPayProxy.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace FusionPayProxy.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PaymentController : ControllerBase
    {
        private readonly IFusionPayService _fusionPayService;
        private readonly ShopifyService _shopifyService;
        private readonly ILogger<PaymentController> _logger;

        public PaymentController(
            IFusionPayService fusionPayService,
            ShopifyService shopifyService,
            ILogger<PaymentController> logger)
        {
            _fusionPayService = fusionPayService;
            _shopifyService = shopifyService;
            _logger = logger;
        }

        [HttpPost("initiate")]
        public async Task<IActionResult> InitiatePayment([FromBody] PaymentRequest request)
        {
            try
            {
                _logger.LogInformation("🚀 Received payment initiation request for order {OrderId}", request.OrderId);

                // Validation des données
                var validationResult = ValidatePaymentRequest(request);
                if (!validationResult.IsValid)
                {
                    _logger.LogWarning("🛑 Invalid payment request: {Errors}",
                        string.Join(", ", validationResult.Errors));
                    return BadRequest(new
                    {
                        success = false,
                        errors = validationResult.Errors,
                        message = "Validation failed"
                    });
                }

                // Créer un enregistrement Shopify
                await _shopifyService.CreateShopifyOrderRecordAsync(
                    request.OrderId,
                    request.OrderNumber ?? $"ORDER_{DateTime.UtcNow.Ticks}",
                    request.Amount,
                    request.CustomerName,
                    request.CustomerPhone,
                    request.CustomerEmail
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
                        returnUrl = result.ReturnUrl,
                        token = result.Token,
                        transactionId = result.TransactionId,
                        orderId = result.OrderId,
                        message = result.Message,
                        expiresAt = result.ExpiresAt
                    });
                }
                else
                {
                    _logger.LogError("❌ Payment initiation failed: {Error}", result.ErrorMessage);

                    // ✅ CORRECTION : Créer un objet dynamique unique
                    object response;

                    if (result.ValidationErrors != null && result.ValidationErrors.Any())
                    {
                        response = new
                        {
                            success = false,
                            error = result.ErrorMessage,
                            message = "Échec de l'initiation du paiement",
                            validationErrors = result.ValidationErrors
                        };
                    }
                    else
                    {
                        response = new
                        {
                            success = false,
                            error = result.ErrorMessage,
                            message = "Échec de l'initiation du paiement"
                        };
                    }

                    return BadRequest(response);
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
                    success = true,
                    token = token,
                    status = status,
                    transaction = transaction != null ? new
                    {
                        id = transaction.Id,
                        orderId = transaction.ShopifyOrderId,
                        orderNumber = transaction.ShopifyOrderNumber,
                        amount = transaction.Amount,
                        customerPhone = transaction.CustomerPhone,
                        customerName = transaction.CustomerName,
                        status = transaction.Status,
                        createdAt = transaction.CreatedAt,
                        paidAt = transaction.PaidAt
                    } : null,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Error verifying payment");
                return StatusCode(500, new
                {
                    success = false,
                    error = ex.Message,
                    message = "Error verifying payment"
                });
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
                    success = true,
                    count = transactions.Count,
                    transactions = transactions.Select(t => new
                    {
                        id = t.Id,
                        orderId = t.ShopifyOrderId,
                        orderNumber = t.ShopifyOrderNumber,
                        token = t.FusionPayToken,
                        amount = t.Amount,
                        status = t.Status,
                        customerName = t.CustomerName,
                        customerPhone = t.CustomerPhone,
                        createdAt = t.CreatedAt,
                        updatedAt = t.UpdatedAt
                    }),
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Error getting pending transactions");
                return StatusCode(500, new
                {
                    success = false,
                    error = ex.Message,
                    message = "Error getting pending transactions"
                });
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

        [HttpGet("test")]
        public IActionResult TestEndpoint()
        {
            return Ok(new
            {
                success = true,
                message = "PaymentController is working",
                timestamp = DateTime.UtcNow,
                corsConfigured = true,
                apiVersion = "1.0.0"
            });
        }

        // ========== MÉTHODES PRIVÉES ==========

        private (bool IsValid, List<string> Errors) ValidatePaymentRequest(PaymentRequest request)
        {
            var errors = new List<string>();

            if (request.Amount <= 200)
                errors.Add("Le montant doit être supérieur à 200 FCFA");

            if (string.IsNullOrWhiteSpace(request.CustomerPhone))
                errors.Add("Le numéro de téléphone du client est requis");
            else if (request.CustomerPhone.Length < 8)
                errors.Add("Le numéro de téléphone doit contenir au moins 8 chiffres");

            if (string.IsNullOrWhiteSpace(request.CustomerName))
                errors.Add("Le nom du client est requis");

            if (string.IsNullOrWhiteSpace(request.OrderId))
                errors.Add("L'ID de commande est requis");

            return (!errors.Any(), errors);
        }
    }
}
