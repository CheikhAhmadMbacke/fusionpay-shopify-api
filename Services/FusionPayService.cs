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
        private readonly string _apiUrl;

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
            _apiUrl = _settings.ApiUrl;

            // Configuration du HttpClient pour FusionPay
            ConfigureHttpClient();
        }

        private void ConfigureHttpClient()
        {
            _httpClient.DefaultRequestHeaders.Clear();
            // SUPPRIME ces 2 lignes :
            // _httpClient.DefaultRequestHeaders.Add(
            //     "moneyfusion-private-key",
            //     _settings.ApiKey
            // );

            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json")
            );
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public async Task<PaymentResponse> InitiatePaymentAsync(PaymentRequest request)
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync();

            try
            {
                _logger.LogInformation("🔄 Initiating FusionPay payment for order {OrderId}", request.OrderId);

                // 1. Créer transaction en base
                var dbTransaction = new Transaction
                {
                    ShopifyOrderId = request.OrderId,
                    ShopifyOrderNumber = request.OrderNumber,
                    CustomerPhone = FormatPhoneNumber(request.CustomerPhone),
                    CustomerName = request.CustomerName,
                    Amount = request.Amount,
                    Status = "pending",
                    CreatedAt = DateTime.UtcNow
                };

                await _dbContext.Transactions.AddAsync(dbTransaction);
                await _dbContext.SaveChangesAsync();

                _logger.LogDebug("📝 Transaction created in DB with ID: {Id}", dbTransaction.Id);

                // 2. Préparer la requête pour FusionPay (FORMAT EXACT DE LEUR DOC)
                var fusionPayRequest = new
                {
                    totalPrice = (int)request.Amount, // Montant en unités (200 = 200 FCFA)

                    // ARTICLE OBLIGATOIRE - format spécifique FusionPay
                    article = new[]
                    {
                        new Dictionary<string, int>
                        {
                            [request.ProductName] = (int)request.Amount
                        }
                    },

                    // CHAMPS OBLIGATOIRES
                    numeroSend = FormatPhoneNumber(request.CustomerPhone),
                    nomclient = request.CustomerName,

                    // INFORMATIONS POUR LE WEBHOOK
                    personal_Info = new[]
                    {
                        new
                        {
                            userId = "shopify",
                            orderId = request.OrderId,
                            transactionId = dbTransaction.Id,
                            orderNumber = request.OrderNumber
                        }
                    },

                    // URLS DE CALLBACK
                    return_url = request.ReturnUrl,
                    webhook_url = $"{_settings.YourApiBaseUrl}/api/webhook/fusionpay"
                };

                var jsonRequest = JsonSerializer.Serialize(fusionPayRequest);
                _logger.LogDebug("📤 Sending to FusionPay: {Json}", jsonRequest);

                // 3. Appeler l'API FusionPay
                var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(_apiUrl, content);

                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogDebug("📥 FusionPay response: {Response}", responseContent);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("❌ FusionPay API error: {StatusCode} - {Content}",
                        response.StatusCode, responseContent);

                    dbTransaction.ErrorMessage = $"FusionPay error: {response.StatusCode}";
                    dbTransaction.UpdatedAt = DateTime.UtcNow;
                    await _dbContext.SaveChangesAsync();
                    await transaction.RollbackAsync();

                    return new PaymentResponse
                    {
                        Success = false,
                        ErrorMessage = $"Erreur FusionPay: {response.StatusCode}",
                        Timestamp = DateTime.UtcNow
                    };
                }

                // 4. Parser la réponse
                var responseData = JsonSerializer.Deserialize<JsonElement>(responseContent);

                var paymentResponse = new PaymentResponse
                {
                    Success = responseData.GetProperty("statut").GetBoolean(),
                    PaymentUrl = responseData.GetProperty("url").GetString() ?? "",
                    Token = responseData.GetProperty("token").GetString() ?? "",
                    Message = responseData.GetProperty("message").GetString() ?? "",
                    TransactionId = dbTransaction.Id,
                    Timestamp = DateTime.UtcNow
                };

                if (paymentResponse.Success)
                {
                    // 5. Mettre à jour la transaction avec le token
                    dbTransaction.FusionPayToken = paymentResponse.Token;
                    dbTransaction.UpdatedAt = DateTime.UtcNow;
                    await _dbContext.SaveChangesAsync();

                    await transaction.CommitAsync();
                    _logger.LogInformation("✅ Payment initiated successfully. Token: {Token}", paymentResponse.Token);
                }
                else
                {
                    dbTransaction.ErrorMessage = paymentResponse.Message;
                    dbTransaction.UpdatedAt = DateTime.UtcNow;
                    await _dbContext.SaveChangesAsync();
                    await transaction.RollbackAsync();

                    _logger.LogWarning("⚠️ FusionPay returned failure: {Message}", paymentResponse.Message);
                }

                return paymentResponse;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "💥 Error initiating FusionPay payment");

                return new PaymentResponse
                {
                    Success = false,
                    ErrorMessage = $"Exception: {ex.Message}",
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

                // 2. Chercher la transaction
                var transaction = await _dbContext.Transactions
                    .FirstOrDefaultAsync(t => t.FusionPayToken == webhook.TokenPay);

                if (transaction == null)
                {
                    _logger.LogWarning("❌ Transaction not found for token: {Token}", webhook.TokenPay);
                    return false;
                }

                // 3. Vérifier si déjà traité (GESTION DES DOUBLONS - CRITIQUE)
                if (transaction.IsProcessed && transaction.WebhookEvent == webhook.Event)
                {
                    _logger.LogInformation("🔄 Duplicate webhook ignored for token: {Token}", webhook.TokenPay);

                    // Marquer le log comme doublon
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

                    return false;
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
                //transaction.TransactionNumber = webhook.NumeroTransaction;
                transaction.Fees = webhook.Frais;
                //transaction.PaymentMethod = GetPaymentMethodFromWebhook(webhook);
                transaction.IsProcessed = true;
                transaction.ProcessedAt = DateTime.UtcNow;
                transaction.UpdatedAt = DateTime.UtcNow;

                if (webhook.Event == "payin.session.completed")
                {
                    transaction.PaidAt = DateTime.UtcNow;
                }

                // 6. Sauvegarder les données brutes
                //transaction.RawWebhookData = JsonSerializer.Serialize(webhook.RawData);

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
                string verifyUrl = $"https://www.pay.moneyfusion.net/paiementNotif/{token}";
                _logger.LogDebug("🔍 Verifying payment status for token: {Token}", token);

                var response = await _httpClient.GetAsync(verifyUrl);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = JsonSerializer.Deserialize<JsonElement>(content);

                    if (data.TryGetProperty("data", out var dataElement) &&
                        dataElement.TryGetProperty("statut", out var statusElement))
                    {
                        string status = statusElement.GetString() ?? "unknown";
                        _logger.LogDebug("📊 Payment status: {Status}", status);
                        return status;
                    }
                }

                return "error";
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
                HttpMethod = "POST"
            };

            await _dbContext.WebhookLogs.AddAsync(webhookLog);
            await _dbContext.SaveChangesAsync();
        }

        private string FormatPhoneNumber(string phone)
        {
            // Format FusionPay: "01010101" (chiffres seulement)
            return new string(phone.Where(char.IsDigit).ToArray());
        }

/*        private string GetPaymentMethodFromWebhook(FusionPayWebhookRequest webhook)
        {
            // Extraire depuis le webhook si disponible
            if (webhook.RawData.HasValue &&
                webhook.RawData.Value.TryGetProperty("moyen", out var moyen))
            {
                return moyen.GetString() ?? "unknown";
            }
            return "unknown";
        }*/
    }

    public class FusionPaySettings
    {
        public string ApiUrl { get; set; } = string.Empty;
        public string YourApiBaseUrl { get; set; } = string.Empty;
    }
}
