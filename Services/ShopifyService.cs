using FusionPayProxy.Data;
using FusionPayProxy.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FusionPayProxy.Services
{
    public class ShopifyService
    {
        private readonly AppDbContext _dbContext;
        private readonly ILogger<ShopifyService> _logger;
        private readonly ShopifySettings _settings;

        public ShopifyService(
            AppDbContext dbContext,
            ILogger<ShopifyService> logger,
            IOptions<ShopifySettings> settings)
        {
            _dbContext = dbContext;
            _logger = logger;
            _settings = settings.Value;
        }

        public async Task CreateShopifyOrderRecordAsync(
            string orderId,
            string orderNumber,
            decimal amount,
            string customerName = "",
            string customerPhone = "",
            string customerEmail = "")
        {
            try
            {
                // Vérifier si l'ordre existe déjà
                var existingOrder = await _dbContext.ShopifyOrders
                    .FirstOrDefaultAsync(o => o.OrderId == orderId);

                if (existingOrder != null)
                {
                    _logger.LogInformation("📝 Shopify order already exists: {OrderId}", orderId);
                    return;
                }

                // Créer un nouvel enregistrement
                var shopifyOrder = new ShopifyOrder
                {
                    OrderId = orderId,
                    OrderNumber = orderNumber,
                    TotalPrice = amount,
                    CustomerName = customerName,
                    CustomerPhone = customerPhone,
                    CustomerEmail = customerEmail,
                    Currency = "XOF",
                    FinancialStatus = "pending",
                    FulfillmentStatus = "unfulfilled",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    WhatsAppSent = false
                };

                await _dbContext.ShopifyOrders.AddAsync(shopifyOrder);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("✅ Shopify order record created: {OrderId} - {OrderNumber}",
                    orderId, orderNumber);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error creating Shopify order record for {OrderId}", orderId);
                // Ne pas throw pour ne pas bloquer le flux de paiement
            }
        }

        public async Task UpdateShopifyOrderStatusAsync(string orderId, string status)
        {
            try
            {
                var order = await _dbContext.ShopifyOrders
                    .FirstOrDefaultAsync(o => o.OrderId == orderId);

                if (order != null)
                {
                    order.FinancialStatus = status;
                    order.UpdatedAt = DateTime.UtcNow;
                    await _dbContext.SaveChangesAsync();

                    _logger.LogInformation("✅ Shopify order {OrderId} updated to status: {Status}",
                        orderId, status);
                }
                else
                {
                    _logger.LogWarning("⚠️ Shopify order not found: {OrderId}", orderId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error updating Shopify order status for {OrderId}", orderId);
            }
        }

        public async Task MarkWhatsAppSentAsync(string orderId)
        {
            try
            {
                var order = await _dbContext.ShopifyOrders
                    .FirstOrDefaultAsync(o => o.OrderId == orderId);

                if (order != null)
                {
                    order.WhatsAppSent = true;
                    order.WhatsAppSentAt = DateTime.UtcNow;
                    order.UpdatedAt = DateTime.UtcNow;
                    await _dbContext.SaveChangesAsync();

                    _logger.LogInformation("✅ WhatsApp notification marked as sent for order: {OrderId}", orderId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error marking WhatsApp as sent for order {OrderId}", orderId);
            }
        }
    }

    public class ShopifySettings
    {
        public string ShopDomain { get; set; } = string.Empty;
        public string AccessToken { get; set; } = string.Empty;
    }
}
