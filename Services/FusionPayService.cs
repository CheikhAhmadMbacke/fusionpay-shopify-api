using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FusionPayProxy.Data;
using FusionPayProxy.Models;
using FusionPayProxy.Models.Requests;
using FusionPayProxy.Models.Responses;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FusionPayProxy.Services
{
    public class FusionPayService : IFusionPayService
    {
        private readonly HttpClient _httpClient;
        private readonly AppDbContext _dbContext;
        private readonly ILogger<FusionPayService> _logger;
        private readonly FusionPaySettings _settings;

        public FusionPayService(
            HttpClient httpClient,
            AppDbContext dbContext,
            ILogger<FusionPayService> logger,
            IOptions<FusionPaySettings> settings)
        {
            _httpClient = httpClient;
            _dbContext = dbContext;
            _logger = logger;
            _settings = settings.Value;

            ConfigureHttpClient();
            _logger.LogInformation("🔧 FusionPay Settings Loaded:");
            _logger.LogInformation("   - ApiBaseUrl: {ApiBaseUrl}", _settings.ApiBaseUrl);
            _logger.LogInformation("   - YourApiBaseUrl: {YourApiBaseUrl}", _settings.YourApiBaseUrl);
        }

        private void ConfigureHttpClient()
        {
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json")
            );
            _httpClient.Timeout = TimeSpan.FromSeconds(60);
        }

        public async Task<PaymentResponse> InitiatePaymentAsync(PaymentRequest request)
        {
            try
            {
                _logger.LogInformation("🔄 Initiating FusionPay payment for order {OrderId}", request.OrderId);

                // ==================== VALIDATION DES DONNÉES ====================
                if (!request.Validate(out var validationErrors))
                {
                    _logger.LogWarning("🛑 Invalid payment request for order {OrderId}: {Errors}",
                        request.OrderId, string.Join(", ", validationErrors));

                    return new PaymentResponse
                    {
                        Success = false,
                        ErrorMessage = "Données de paiement invalides",
                        ValidationErrors = validationErrors,
                        OrderId = request.OrderId,
                        Timestamp = DateTime.UtcNow
                    };
                }

                // ==================== ÉTAPE 1: CRÉER LA TRANSACTION ====================
                var dbTransaction = new Transaction
                {
                    ShopifyOrderId = request.OrderId,
                    ShopifyOrderNumber = request.OrderNumber ?? $"ORDER_{DateTime.UtcNow.Ticks}",
                    CustomerPhone = request.GetFormattedPhone(),
                    CustomerName = request.CustomerName,
                    CustomerEmail = request.CustomerEmail,
                    Amount = request.Amount,
                    Status = "initiating",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    FusionPayToken = null // ✅ DÉFINIR À NULL AU DÉPART
                };

                await _dbContext.Transactions.AddAsync(dbTransaction);
                await _dbContext.SaveChangesAsync();

                _logger.LogDebug("📝 Transaction created in DB with ID: {Id}", dbTransaction.Id);

                // ==================== ÉTAPE 2: PRÉPARER LA REQUÊTE FUSIONPAY ====================
                var fusionPayRequest = new
                {
                    totalPrice = (int)request.Amount,
                    article = request.GetFusionPayArticleFormat(),
                    numeroSend = request.GetFormattedPhone(),
                    nomclient = request.CustomerName,
                    personal_Info = request.GetFusionPayPersonalInfo(dbTransaction.Id),
                    return_url = $"{_settings.YourApiBaseUrl}/thank-you.html?orderId={Uri.EscapeDataString(request.OrderId)}",
                    webhook_url = $"{_settings.YourApiBaseUrl}/api/webhook/fusionpay"
                };

                var jsonRequest = JsonSerializer.Serialize(fusionPayRequest);
                _logger.LogDebug("📤 Sending to FusionPay: {Json}", jsonRequest);

                // ==================== ÉTAPE 3: APPELER FUSIONPAY ====================
                // ✅ VÉRIFIER LA CONFIGURATION FUSIONPAY
                if (string.IsNullOrEmpty(_settings.ApiBaseUrl))
                {
                    _logger.LogError("❌ FusionPay API URL is not configured in settings");

                    dbTransaction.ErrorMessage = "Configuration FusionPay manquante";
                    dbTransaction.Status = "failed";
                    dbTransaction.UpdatedAt = DateTime.UtcNow;
                    await _dbContext.SaveChangesAsync();

                    return new PaymentResponse
                    {
                        Success = false,
                        ErrorMessage = "Configuration FusionPay manquante. Vérifiez appsettings.json",
                        OrderId = request.OrderId,
                        TransactionId = dbTransaction.Id,
                        Timestamp = DateTime.UtcNow
                    };
                }

                var fusionPayUrl = _settings.ApiBaseUrl.Trim();

                // S'assurer que l'URL se termine par /
                if (!fusionPayUrl.EndsWith("/"))
                {
                    fusionPayUrl += "/";
                }

                _logger.LogInformation("🌍 Calling FusionPay API at: {Url}", fusionPayUrl);

                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(45);
                httpClient.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("application/json"));

                var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

                try
                {
                    var response = await httpClient.PostAsync(fusionPayUrl, content);
                    var responseContent = await response.Content.ReadAsStringAsync();
                    _logger.LogDebug("📥 FusionPay raw response: {Response}", responseContent);

                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogError("❌ FusionPay API error: {StatusCode} - {Content}",
                            response.StatusCode, responseContent);

                        dbTransaction.ErrorMessage = $"HTTP {response.StatusCode}";
                        dbTransaction.Status = "failed";
                        dbTransaction.UpdatedAt = DateTime.UtcNow;
                        await _dbContext.SaveChangesAsync();

                        return new PaymentResponse
                        {
                            Success = false,
                            ErrorMessage = $"Erreur FusionPay: {response.StatusCode}",
                            OrderId = request.OrderId,
                            TransactionId = dbTransaction.Id,
                            Timestamp = DateTime.UtcNow
                        };
                    }

                    JsonElement responseData;
                    try
                    {
                        responseData = JsonSerializer.Deserialize<JsonElement>(responseContent);
                    }
                    catch (JsonException jsonEx)
                    {
                        _logger.LogError(jsonEx, "❌ Failed to parse FusionPay response: {Content}", responseContent);

                        dbTransaction.ErrorMessage = "Invalid JSON response from FusionPay";
                        dbTransaction.Status = "failed";
                        dbTransaction.UpdatedAt = DateTime.UtcNow;
                        await _dbContext.SaveChangesAsync();

                        return new PaymentResponse
                        {
                            Success = false,
                            ErrorMessage = "Réponse invalide de FusionPay",
                            OrderId = request.OrderId,
                            TransactionId = dbTransaction.Id,
                            Timestamp = DateTime.UtcNow
                        };
                    }

                    // Vérifier les champs obligatoires
                    if (!responseData.TryGetProperty("statut", out var statusProp) ||
                        !responseData.TryGetProperty("token", out var tokenProp) ||
                        !responseData.TryGetProperty("url", out var urlProp))
                    {
                        _logger.LogError("❌ FusionPay response missing required fields: {Content}", responseContent);

                        dbTransaction.ErrorMessage = "Missing fields in FusionPay response";
                        dbTransaction.Status = "failed";
                        dbTransaction.UpdatedAt = DateTime.UtcNow;
                        await _dbContext.SaveChangesAsync();

                        return new PaymentResponse
                        {
                            Success = false,
                            ErrorMessage = "Réponse incomplète de FusionPay",
                            OrderId = request.OrderId,
                            TransactionId = dbTransaction.Id,
                            Timestamp = DateTime.UtcNow
                        };
                    }

                    var isSuccess = statusProp.GetBoolean();
                    var token = tokenProp.GetString() ?? "";
                    var paymentUrl = urlProp.GetString() ?? "";
                    var message = responseData.TryGetProperty("message", out var msgProp)
                        ? msgProp.GetString() ?? ""
                        : "";

                    // ==================== METTRE À JOUR LA TRANSACTION ====================
                    dbTransaction.FusionPayToken = token; // ✅ MAINTENANT ON A LE TOKEN
                    dbTransaction.Status = isSuccess ? "pending" : "failed";
                    dbTransaction.UpdatedAt = DateTime.UtcNow;

                    if (!isSuccess)
                    {
                        dbTransaction.ErrorMessage = message;
                    }

                    await _dbContext.SaveChangesAsync();

                    // ==================== PRÉPARER LA RÉPONSE ====================
                    if (isSuccess)
                    {
                        _logger.LogInformation("✅ Payment initiated successfully for order {OrderId}. Token: {Token}",
                            request.OrderId, token);

                        string completeReturnUrl = request.GenerateThankYouUrl(token, $"{_settings.YourApiBaseUrl}/thank-you.html");

                        return new PaymentResponse
                        {
                            Success = true,
                            PaymentUrl = paymentUrl,
                            ReturnUrl = completeReturnUrl,
                            Token = token,
                            Message = message,
                            TransactionId = dbTransaction.Id,
                            OrderId = request.OrderId,
                            Timestamp = DateTime.UtcNow,
                            ExpiresAt = DateTime.UtcNow.AddMinutes(30)
                        };
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ FusionPay returned failure for order {OrderId}: {Message}",
                            request.OrderId, message);

                        return new PaymentResponse
                        {
                            Success = false,
                            ErrorMessage = message,
                            TransactionId = dbTransaction.Id,
                            OrderId = request.OrderId,
                            Timestamp = DateTime.UtcNow
                        };
                    }
                }
                catch (HttpRequestException httpEx)
                {
                    _logger.LogError(httpEx, "❌ HTTP request error to FusionPay");

                    dbTransaction.ErrorMessage = $"HTTP Error: {httpEx.Message}";
                    dbTransaction.Status = "failed";
                    dbTransaction.UpdatedAt = DateTime.UtcNow;
                    await _dbContext.SaveChangesAsync();

                    return new PaymentResponse
                    {
                        Success = false,
                        ErrorMessage = $"Erreur réseau: {httpEx.Message}",
                        OrderId = request.OrderId,
                        TransactionId = dbTransaction.Id,
                        Timestamp = DateTime.UtcNow
                    };
                }
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex, "⏱️ TIMEOUT: FusionPay API call took too long (45s) for order {OrderId}",
                    request.OrderId);

                return new PaymentResponse
                {
                    Success = false,
                    ErrorMessage = "FusionPay ne répond pas (timeout 45s). Veuillez réessayer.",
                    OrderId = request.OrderId,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Error initiating FusionPay payment for order {OrderId}",
                    request.OrderId);

                return new PaymentResponse
                {
                    Success = false,
                    ErrorMessage = $"Erreur interne: {ex.Message}",
                    OrderId = request.OrderId,
                    Timestamp = DateTime.UtcNow
                };
            }
        }

        public async Task<bool> HandleWebhookAsync(FusionPayWebhookRequest webhook)
        {
            try
            {
                _logger.LogInformation("📨 Received FusionPay webhook: {Event} - {Token}",
                    webhook.Event, webhook.TokenPay);

                // 1. Log du webhook
                await LogWebhookAsync(webhook);

                // 2. Chercher la transaction avec retry
                var transaction = await FindTransactionWithRetryAsync(webhook.TokenPay);
                if (transaction == null)
                {
                    _logger.LogError("❌ Transaction not found for token: {Token}", webhook.TokenPay);
                    return false;
                }

                // 3. Vérifier les doublons
                if (IsDuplicateWebhook(transaction, webhook.Event))
                {
                    _logger.LogInformation("🔄 Duplicate webhook ignored for token: {Token}", webhook.TokenPay);
                    await MarkWebhookAsDuplicateAsync(webhook.TokenPay, webhook.Event);
                    return true;
                }

                // 4. Traiter le webhook
                await ProcessWebhookAsync(transaction, webhook);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Error processing webhook");
                return false;
            }
        }

        private async Task<Transaction?> FindTransactionWithRetryAsync(string tokenPay)
        {
            var transaction = await _dbContext.Transactions
                .FirstOrDefaultAsync(t => t.FusionPayToken == tokenPay);

            if (transaction == null)
            {
                _logger.LogWarning("⚠️ Transaction not found, waiting 3 seconds...");
                await Task.Delay(3000);

                transaction = await _dbContext.Transactions
                    .FirstOrDefaultAsync(t => t.FusionPayToken == tokenPay);
            }

            return transaction;
        }

        private bool IsDuplicateWebhook(Transaction transaction, string webhookEvent)
        {
            return transaction.IsProcessed && transaction.WebhookEvent == webhookEvent;
        }

        private async Task MarkWebhookAsDuplicateAsync(string tokenPay, string eventType)
        {
            var webhookLog = await _dbContext.WebhookLogs
                .Where(w => w.TokenPay == tokenPay && w.EventType == eventType)
                .OrderByDescending(w => w.ReceivedAt)
                .FirstOrDefaultAsync();

            if (webhookLog != null)
            {
                webhookLog.IsDuplicate = true;
                webhookLog.ProcessingResult = "Duplicate ignored";
                await _dbContext.SaveChangesAsync();
            }
        }

        private async Task ProcessWebhookAsync(Transaction transaction, FusionPayWebhookRequest webhook)
        {
            // Déterminer le statut Shopify
            string shopifyStatus = webhook.Event switch
            {
                "payin.session.completed" => "paid",
                "payin.session.cancelled" => "failed",
                "payin.session.pending" => "pending",
                _ => "unknown"
            };

            // Mettre à jour la transaction
            transaction.Status = shopifyStatus;
            transaction.WebhookEvent = webhook.Event;
            transaction.Fees = webhook.Frais;
            transaction.IsProcessed = true;
            transaction.ProcessedAt = DateTime.UtcNow;
            transaction.UpdatedAt = DateTime.UtcNow;

            if (webhook.Event == "payin.session.completed")
            {
                transaction.PaidAt = DateTime.UtcNow;
            }

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("✅ Webhook processed. Transaction {Id} updated to {Status}",
                transaction.Id, shopifyStatus);
        }

        public async Task<string> VerifyPaymentStatusAsync(string token)
        {
            try
            {
                string verifyUrl = $"https://www.pay.moneyfusion.net/paiementNotif/{token}";
                _logger.LogDebug("🔍 Verifying payment status at: {Url}", verifyUrl);

                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(30);

                var response = await httpClient.GetAsync(verifyUrl);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    _logger.LogDebug("📊 Payment status response: {Content}", content);

                    var responseData = JsonSerializer.Deserialize<JsonElement>(content);

                    if (responseData.TryGetProperty("statut", out var statusElement) &&
                        statusElement.GetBoolean())
                    {
                        if (responseData.TryGetProperty("data", out var dataElement))
                        {
                            if (dataElement.TryGetProperty("statut", out var paymentStatus))
                            {
                                return paymentStatus.GetString() ?? "unknown";
                            }
                        }
                        return "pending";
                    }
                    return "pending";
                }

                _logger.LogWarning("⚠️ Failed to verify payment status. HTTP {StatusCode}", response.StatusCode);
                return "error";
            }
            catch (TaskCanceledException)
            {
                _logger.LogWarning("⏱️ Timeout verifying payment status for token: {Token}", token);
                return "timeout";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Error verifying payment status");
                return "error";
            }
        }

        public async Task<Transaction?> GetTransactionByTokenAsync(string token)
        {
            return await _dbContext.Transactions
                .FirstOrDefaultAsync(t => t.FusionPayToken == token);
        }

        public async Task<List<Transaction>> GetPendingTransactionsAsync()
        {
            return await _dbContext.Transactions
                .Where(t => t.Status == "pending" && !t.IsProcessed)
                .OrderBy(t => t.CreatedAt)
                .Take(50)
                .ToListAsync();
        }

        // ========== MÉTHODES PRIVÉES ==========

        private async Task LogWebhookAsync(FusionPayWebhookRequest webhook)
        {
            var webhookLog = new WebhookLog
            {
                EventType = webhook.Event,
                TokenPay = webhook.TokenPay,
                Payload = JsonSerializer.Serialize(webhook),
                ReceivedAt = DateTime.UtcNow,
                HttpMethod = "POST",
                ProcessingResult = "Received, processing...",
                IsDuplicate = false
            };

            await _dbContext.WebhookLogs.AddAsync(webhookLog);
            await _dbContext.SaveChangesAsync();
        }
    }

    public class FusionPaySettings
    {
        public string ApiBaseUrl { get; set; } = string.Empty;
        public string YourApiBaseUrl { get; set; } = string.Empty;
    }
}
