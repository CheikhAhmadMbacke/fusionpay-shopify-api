using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FusionPayProxy.Data;
using FusionPayProxy.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FusionPayProxy.Services
{
    public class ShopifyService
    {
        private readonly HttpClient _httpClient;
        private readonly AppDbContext _dbContext;
        private readonly ILogger<ShopifyService> _logger;
        private readonly ShopifySettings _settings;
        private readonly bool _isShopifyConfigured;

        public ShopifyService(
            HttpClient httpClient,
            AppDbContext dbContext,
            ILogger<ShopifyService> logger,
            IOptions<ShopifySettings> settings)
        {
            _httpClient = httpClient;
            _dbContext = dbContext;
            _logger = logger;
            _settings = settings.Value;
            
            _isShopifyConfigured = !string.IsNullOrEmpty(_settings.ShopDomain) && 
                                  !string.IsNullOrEmpty(_settings.AccessToken);
            
            ConfigureHttpClient();
        }

        private void ConfigureHttpClient()
        {
            if (!_isShopifyConfigured)
            {
                _logger.LogWarning("⚠️ Shopify n'est pas configuré. StoreName ou AccessToken manquant.");
                return;
            }

            try
            {
                // Deux formats possibles pour le domaine :
                // 1. "afrokingvap.com" → on utilise le format admin API standard
                // 2. "nom-boutique.myshopify.com" → format direct
                
                string baseUrl;
                
                if (_settings.ShopDomain.Contains(".myshopify.com"))
                {
                    // Format: nom-boutique.myshopify.com
                    baseUrl = $"https://{_settings.ShopDomain}/admin/api/2024-01/";
                }
                else
                {
                    // Format: afrokingvap.com (domaine personnalisé)
                    // On doit récupérer le nom de la boutique depuis le domaine
                    // Pour l'instant, on utilise un format générique
                    // Dans la vraie configuration, le client doit fournir le nom de boutique Shopify
                    baseUrl = $"https://{_settings.ShopDomain}/admin/api/2024-01/";
                }

                // Validation de l'URL
                if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
                {
                    _httpClient.BaseAddress = uri;
                    _httpClient.DefaultRequestHeaders.Add("X-Shopify-Access-Token", _settings.AccessToken);
                    _httpClient.DefaultRequestHeaders.Accept.Add(
                        new MediaTypeWithQualityHeaderValue("application/json")
                    );
                    
                    _logger.LogInformation($"✅ Shopify API configuré pour: {_settings.ShopDomain}");
                }
                else
                {
                    _logger.LogError($"❌ URL Shopify invalide: {baseUrl}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Erreur de configuration Shopify HttpClient");
            }
        }

        public async Task<bool> UpdateOrderStatusAsync(string orderId, string status)
        {
            if (!_isShopifyConfigured)
            {
                _logger.LogWarning("⚠️ Shopify non configuré - Simulation de mise à jour pour l'ordre {OrderId}", orderId);
                return await SimulateShopifyUpdate(orderId, status);
            }

            try
            {
                _logger.LogInformation("🔄 Mise à jour de l'ordre Shopify {OrderId} vers {Status}", orderId, status);

                var updateData = new
                {
                    order = new
                    {
                        id = orderId,
                        financial_status = status
                    }
                };

                var json = JsonSerializer.Serialize(updateData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PutAsync($"orders/{orderId}.json", content);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("✅ Ordre Shopify mis à jour avec succès");

                    // Mettre à jour dans notre base de données
                    await UpdateLocalOrderStatus(orderId, status);
                    
                    return true;
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    _logger.LogError("❌ Échec de la mise à jour de l'ordre Shopify: {StatusCode} - {Error}", 
                        response.StatusCode, error);
                    
                    // On tente quand même de mettre à jour localement
                    await UpdateLocalOrderStatus(orderId, status);
                    
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Erreur lors de la mise à jour de l'ordre Shopify");
                
                // En cas d'erreur, on met à jour localement pour garder une trace
                await UpdateLocalOrderStatus(orderId, status);
                
                return false;
            }
        }

        private async Task<bool> UpdateLocalOrderStatus(string orderId, string status)
        {
            try
            {
                var shopifyOrder = await _dbContext.ShopifyOrders
                    .FirstOrDefaultAsync(o => o.OrderId == orderId);

                if (shopifyOrder != null)
                {
                    shopifyOrder.FinancialStatus = status;
                    shopifyOrder.UpdatedAt = DateTime.UtcNow;
                    await _dbContext.SaveChangesAsync();
                    _logger.LogInformation("📝 Statut mis à jour localement pour l'ordre {OrderId}: {Status}", orderId, status);
                }
                else
                {
                    // Créer un enregistrement si non existant
                    shopifyOrder = new ShopifyOrder
                    {
                        OrderId = orderId,
                        OrderNumber = $"SYNC_{orderId}",
                        FinancialStatus = status,
                        FulfillmentStatus = "unfulfilled",
                        TotalPrice = 0,
                        Currency = "XOF",
                        CustomerEmail = "sync@afrokingvap.com",
                        CustomerPhone = "000000000",
                        CustomerName = "Client Sync",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                        WhatsAppSent = false
                    };
                    
                    await _dbContext.ShopifyOrders.AddAsync(shopifyOrder);
                    await _dbContext.SaveChangesAsync();
                    _logger.LogInformation("📝 Enregistrement Shopify créé localement pour l'ordre {OrderId}", orderId);
                }
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Erreur lors de la mise à jour locale de l'ordre");
                return false;
            }
        }

        private async Task<bool> SimulateShopifyUpdate(string orderId, string status)
        {
            _logger.LogInformation("🔧 Simulation de mise à jour Shopify pour l'ordre {OrderId} -> {Status}", orderId, status);
            
            // Mettre à jour localement pour les tests
            await UpdateLocalOrderStatus(orderId, status);
            
            // Simuler un délai d'API
            await Task.Delay(100);
            
            return true;
        }

        public async Task<bool> CreateShopifyOrderRecordAsync(string orderId, string orderNumber, decimal totalPrice)
        {
            try
            {
                var existingOrder = await _dbContext.ShopifyOrders
                    .FirstOrDefaultAsync(o => o.OrderId == orderId);

                if (existingOrder != null)
                {
                    _logger.LogInformation("📝 Enregistrement Shopify existe déjà: {OrderId}", orderId);
                    return true;
                }

                var order = new ShopifyOrder
                {
                    OrderId = orderId,
                    OrderNumber = orderNumber,
                    TotalPrice = totalPrice,
                    FinancialStatus = "pending",
                    FulfillmentStatus = "unfulfilled",
                    Currency = "XOF",
                    CustomerEmail = "pending@afrokingvap.com",
                    CustomerPhone = "000000000",
                    CustomerName = "Client En Attente",
                    CreatedAt = DateTime.UtcNow,
                    WhatsAppSent = false
                };

                await _dbContext.ShopifyOrders.AddAsync(order);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("📝 Enregistrement Shopify créé: {OrderNumber}", orderNumber);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Erreur création enregistrement Shopify");
                return false;
            }
        }

        public async Task<ShopifyOrder?> GetOrderAsync(string orderId)
        {
            return await _dbContext.ShopifyOrders
                .FirstOrDefaultAsync(o => o.OrderId == orderId);
        }

        public async Task<bool> MarkWhatsAppSentAsync(string orderId)
        {
            try
            {
                var order = await GetOrderAsync(orderId);
                if (order != null)
                {
                    order.WhatsAppSent = true;
                    order.WhatsAppSentAt = DateTime.UtcNow;
                    await _dbContext.SaveChangesAsync();
                    
                    _logger.LogInformation("✅ WhatsApp marqué comme envoyé pour l'ordre {OrderId}", orderId);
                    return true;
                }
                
                _logger.LogWarning("⚠️ Ordre non trouvé pour marquer WhatsApp: {OrderId}", orderId);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Erreur marquage WhatsApp envoyé");
                return false;
            }
        }
        
        // Méthode utilitaire pour vérifier la connexion Shopify
        public async Task<bool> TestConnectionAsync()
        {
            if (!_isShopifyConfigured)
            {
                _logger.LogWarning("⚠️ Shopify non configuré - Test de connexion impossible");
                return false;
            }
            
            try
            {
                var response = await _httpClient.GetAsync("shop.json");
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("✅ Connexion Shopify API réussie");
                    return true;
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    _logger.LogError("❌ Échec connexion Shopify API: {Error}", error);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Erreur test connexion Shopify");
                return false;
            }
        }
    }

    public class ShopifySettings
    {
        public string ShopDomain { get; set; } = string.Empty;
        public string AccessToken { get; set; } = string.Empty;
    }
}
