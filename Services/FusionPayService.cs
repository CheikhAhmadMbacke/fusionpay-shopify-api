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

            // Configuration du HttpClient pour FusionPay
            ConfigureHttpClient();
        }

        private void ConfigureHttpClient()
        {
            // ✅ PAS D'API KEY pour FusionPay Pay-In
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json")
            );
            _httpClient.Timeout = TimeSpan.FromSeconds(60); // Timeout augmenté pour Render.com

            // IMPORTANT: NE PAS définir BaseAddress ici car nous avons une URL unique
            // Le BaseAddress sera utilisé seulement pour certaines requêtes
        }

        public async Task<PaymentResponse> InitiatePaymentAsync(PaymentRequest request)
        {
            try
            {
                _logger.LogInformation("🔄 Initiating FusionPay payment for order {OrderId}", request.OrderId);

                // ==================== ÉTAPE 1: CRÉER LA TRANSACTION (sans token pour l'instant) ====================
                var dbTransaction = new Transaction
                {
                    ShopifyOrderId = request.OrderId,
                    ShopifyOrderNumber = request.OrderNumber ?? $"ORDER_{DateTime.UtcNow.Ticks}",
                    CustomerPhone = FormatPhoneNumber(request.CustomerPhone),
                    CustomerName = request.CustomerName,
                    Amount = request.Amount,
                    Status = "initiating", // Statut temporaire
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                await _dbContext.Transactions.AddAsync(dbTransaction);
                await _dbContext.SaveChangesAsync();

                _logger.LogDebug("📝 Transaction created in DB with ID: {Id} (pending FusionPay response)", dbTransaction.Id);

                // ==================== ÉTAPE 2: APPELER FUSIONPAY (avec timeout optimisé) ====================
                var fusionPayRequest = new
                {
                    totalPrice = (int)request.Amount,
                    article = new[]
                    {
                        new {
                            name = "Commande AfroKingVap",
                            price = (int)request.Amount
                        }
                    },
                    numeroSend = FormatPhoneNumber(request.CustomerPhone),
                    nomclient = request.CustomerName,
                    personal_info = new[]
                    {
                        new
                        {
                            orderId = request.OrderId,
                            transactionId = dbTransaction.Id
                        }
                    },
                    return_url = request.ReturnUrl,
                    webhook_url = $"{_settings.YourApiBaseUrl}/api/webhook/fusionpay"
                };

                var jsonRequest = JsonSerializer.Serialize(fusionPayRequest);
                _logger.LogDebug("📤 Sending to FusionPay: {Json}", jsonRequest);

                // ✅ CRÉER UN NOUVEAU HTTPCLIENT POUR CETTE REQUÊTE (évite les problèmes de partage)
                using var fusionPayClient = new HttpClient();
                fusionPayClient.Timeout = TimeSpan.FromSeconds(45);
                fusionPayClient.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("application/json")
                );

                var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

                // ✅ UTILISER L'URL COMPLÈTE (pas juste BaseAddress)
                var fusionPayUrl = _settings.ApiBaseUrl;
                _logger.LogDebug("🌍 Calling FusionPay at: {Url}", fusionPayUrl);

                var response = await fusionPayClient.PostAsync(fusionPayUrl, content);
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogDebug("📥 FusionPay raw response: {Response}", responseContent);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("❌ FusionPay API error: {StatusCode} - {Content}",
                        response.StatusCode, responseContent);

                    dbTransaction.ErrorMessage = $"FusionPay HTTP error: {response.StatusCode}";
                    dbTransaction.Status = "failed";
                    dbTransaction.UpdatedAt = DateTime.UtcNow;
                    await _dbContext.SaveChangesAsync();

                    return new PaymentResponse
                    {
                        Success = false,
                        ErrorMessage = $"Erreur FusionPay: {response.StatusCode}",
                        Timestamp = DateTime.UtcNow,
                        TransactionId = dbTransaction.Id
                    };
                }

                // ==================== ÉTAPE 3: PARSER LA RÉPONSE DE FUSIONPAY ====================
                JsonElement responseData;
                try
                {
                    responseData = JsonSerializer.Deserialize<JsonElement>(responseContent);
                }
                catch (JsonException jsonEx)
                {
                    _logger.LogError(jsonEx, "❌ Failed to parse FusionPay response");

                    dbTransaction.ErrorMessage = "Invalid JSON response from FusionPay";
                    dbTransaction.Status = "failed";
                    dbTransaction.UpdatedAt = DateTime.UtcNow;
                    await _dbContext.SaveChangesAsync();

                    return new PaymentResponse
                    {
                        Success = false,
                        ErrorMessage = "Réponse invalide de FusionPay",
                        Timestamp = DateTime.UtcNow,
                        TransactionId = dbTransaction.Id
                    };
                }

                // Vérifier la structure de la réponse
                if (!responseData.TryGetProperty("statut", out var statusProp) ||
                    !responseData.TryGetProperty("token", out var tokenProp) ||
                    !responseData.TryGetProperty("url", out var urlProp))
                {
                    _logger.LogError("❌ FusionPay response missing required fields");

                    dbTransaction.ErrorMessage = "Missing fields in FusionPay response";
                    dbTransaction.Status = "failed";
                    dbTransaction.UpdatedAt = DateTime.UtcNow;
                    await _dbContext.SaveChangesAsync();

                    return new PaymentResponse
                    {
                        Success = false,
                        ErrorMessage = "Réponse incomplète de FusionPay",
                        Timestamp = DateTime.UtcNow,
                        TransactionId = dbTransaction.Id
                    };
                }

                var isSuccess = statusProp.GetBoolean();
                var token = tokenProp.GetString() ?? "";
                var paymentUrl = urlProp.GetString() ?? "";
                var message = responseData.TryGetProperty("message", out var msgProp)
                    ? msgProp.GetString() ?? ""
                    : "";

                // ==================== ÉTAPE 4: METTRE À JOUR LA TRANSACTION AVEC LE TOKEN ====================
                dbTransaction.FusionPayToken = token;
                dbTransaction.Status = isSuccess ? "pending" : "failed";
                dbTransaction.UpdatedAt = DateTime.UtcNow;

                if (!isSuccess)
                {
                    dbTransaction.ErrorMessage = message;
                }

                await _dbContext.SaveChangesAsync();

                if (isSuccess)
                {
                    _logger.LogInformation("✅ Payment initiated successfully. Token: {Token}, URL: {Url}", token, paymentUrl);

                    return new PaymentResponse
                    {
                        Success = true,
                        PaymentUrl = paymentUrl,
                        Token = token,
                        Message = message,
                        TransactionId = dbTransaction.Id,
                        Timestamp = DateTime.UtcNow
                    };
                }
                else
                {
                    _logger.LogWarning("⚠️ FusionPay returned failure: {Message}", message);

                    return new PaymentResponse
                    {
                        Success = false,
                        ErrorMessage = message,
                        Message = message,
                        TransactionId = dbTransaction.Id,
                        Timestamp = DateTime.UtcNow
                    };
                }
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex, "⏱️ TIMEOUT: FusionPay API call took too long (45s)");

                return new PaymentResponse
                {
                    Success = false,
                    ErrorMessage = "FusionPay ne répond pas (timeout 45s). Vérifiez votre connexion.",
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Error initiating FusionPay payment");

                return new PaymentResponse
                {
                    Success = false,
                    ErrorMessage = $"Erreur interne: {ex.Message}",
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

                // 2. Chercher la transaction (avec retry si pas trouvée immédiatement)
                var transaction = await _dbContext.Transactions
                    .FirstOrDefaultAsync(t => t.FusionPayToken == webhook.TokenPay);

                if (transaction == null)
                {
                    // ⚠️ Webhook arrive parfois avant que la transaction soit sauvegardée
                    _logger.LogWarning("⚠️ Transaction not found for token: {Token}, waiting 3 seconds...", webhook.TokenPay);
                    await Task.Delay(3000);

                    transaction = await _dbContext.Transactions
                        .FirstOrDefaultAsync(t => t.FusionPayToken == webhook.TokenPay);

                    if (transaction == null)
                    {
                        _logger.LogError("❌ Transaction still not found after retry for token: {Token}", webhook.TokenPay);

                        // Sauvegarder quand même le webhook pour débogage
                        var errorLog = new WebhookLog
                        {
                            EventType = webhook.Event,
                            TokenPay = webhook.TokenPay,
                            Payload = JsonSerializer.Serialize(webhook),
                            ReceivedAt = DateTime.UtcNow,
                            HttpMethod = "POST",
                            ProcessingResult = "ERROR: Transaction not found",
                            IsDuplicate = false
                        };
                        await _dbContext.WebhookLogs.AddAsync(errorLog);
                        await _dbContext.SaveChangesAsync();

                        return false;
                    }
                }

                // 3. Vérifier si déjà traité (GESTION DES DOUBLONS)
                if (transaction.IsProcessed && transaction.WebhookEvent == webhook.Event)
                {
                    _logger.LogInformation("🔄 Duplicate webhook ignored for token: {Token}", webhook.TokenPay);

                    var webhookLog = await _dbContext.WebhookLogs
                        .Where(w => w.TokenPay == webhook.TokenPay && w.EventType == webhook.Event)
                        .OrderByDescending(w => w.ReceivedAt)
                        .FirstOrDefaultAsync();

                    if (webhookLog != null)
                    {
                        webhookLog.IsDuplicate = true;
                        webhookLog.ProcessingResult = "Duplicate ignored";
                        await _dbContext.SaveChangesAsync();
                    }

                    return true; // ✅ Retourne true car c'est normal
                }

                // 4. Déterminer le statut Shopify
                string shopifyStatus = webhook.Event switch
                {
                    "payin.session.completed" => "paid",
                    "payin.session.cancelled" => "failed",
                    "payin.session.pending" => "pending",
                    _ => "unknown"
                };

                // 5. Mettre à jour la transaction
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

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Error processing webhook");
                return false;
            }
        }

        public async Task<string> VerifyPaymentStatusAsync(string token)
        {
            try
            {
                // URL de vérification selon la documentation FusionPay
                string verifyUrl = $"https://www.pay.moneyfusion.net/paiementNotif/{token}";
                _logger.LogDebug("🔍 Verifying payment status at: {Url}", verifyUrl);

                // Créer un HttpClient dédié pour cette requête
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(30);

                var response = await httpClient.GetAsync(verifyUrl);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    _logger.LogDebug("📊 Payment status response: {Content}", content);

                    // Analyser la réponse
                    var responseData = JsonSerializer.Deserialize<JsonElement>(content);

                    // Vérifier si la réponse est valide
                    if (responseData.TryGetProperty("statut", out var statusElement) &&
                        statusElement.GetBoolean())
                    {
                        if (responseData.TryGetProperty("data", out var dataElement))
                        {
                            if (dataElement.TryGetProperty("statut", out var paymentStatus))
                            {
                                var status = paymentStatus.GetString() ?? "unknown";
                                _logger.LogInformation("✅ Payment status for token {Token}: {Status}", token, status);
                                return status;
                            }
                        }
                        return "pending"; // Statut true mais pas de détail
                    }
                    return "pending"; // Réponse valide mais statut false
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

        private string FormatPhoneNumber(string phone)
        {
            // Format FusionPay: "771234567" (chiffres seulement)
            return new string(phone.Where(char.IsDigit).ToArray());
        }
    }

    public class FusionPaySettings
    {
        public string ApiBaseUrl { get; set; } = string.Empty;
        public string YourApiBaseUrl { get; set; } = string.Empty;
    }
}
