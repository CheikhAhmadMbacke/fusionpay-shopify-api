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

            ConfigureHttpClient();
        }

        private void ConfigureHttpClient()
        {
            string baseUrl = $"https://{_settings.StoreName}.myshopify.com/admin/api/2024-01/";
            _httpClient.BaseAddress = new Uri(baseUrl);
            _httpClient.DefaultRequestHeaders.Add("X-Shopify-Access-Token", _settings.AccessToken);
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json")
            );
        }

        public async Task<bool> UpdateOrderStatusAsync(string orderId, string status)
        {
            try
            {
                _logger.LogInformation("🔄 Updating Shopify order {OrderId} to {Status}", orderId, status);

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
                    _logger.LogInformation("✅ Shopify order updated successfully");

                    // Mettre à jour dans notre base
                    var shopifyOrder = await _dbContext.ShopifyOrders
                        .FirstOrDefaultAsync(o => o.OrderId == orderId);

                    if (shopifyOrder != null)
                    {
                        shopifyOrder.FinancialStatus = status;
                        shopifyOrder.UpdatedAt = DateTime.UtcNow;
                        await _dbContext.SaveChangesAsync();
                    }

                    return true;
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    _logger.LogError("❌ Failed to update Shopify order: {Error}", error);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Error updating Shopify order");
                return false;
            }
        }

        public async Task<bool> CreateShopifyOrderRecordAsync(string orderId, string orderNumber, decimal totalPrice)
        {
            try
            {
                var order = new ShopifyOrder
                {
                    OrderId = orderId,
                    OrderNumber = orderNumber,
                    TotalPrice = totalPrice,
                    FinancialStatus = "pending",
                    CreatedAt = DateTime.UtcNow
                };

                await _dbContext.ShopifyOrders.AddAsync(order);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("📝 Shopify order record created: {OrderNumber}", orderNumber);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Error creating Shopify order record");
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
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Error marking WhatsApp as sent");
                return false;
            }
        }
    }

    public class ShopifySettings
    {
        public string StoreName { get; set; } = string.Empty;
        public string AccessToken { get; set; } = string.Empty;
    }
}
