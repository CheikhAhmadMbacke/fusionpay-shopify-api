using FusionPayProxy.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Reflection.Emit;

namespace FusionPayProxy.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Transaction> Transactions { get; set; }
        public DbSet<WebhookLog> WebhookLogs { get; set; }
        public DbSet<ShopifyOrder> ShopifyOrders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configuration Transaction
            modelBuilder.Entity<Transaction>(entity =>
            {
                entity.HasIndex(t => t.FusionPayToken).IsUnique();
                entity.HasIndex(t => t.ShopifyOrderId);
                entity.HasIndex(t => t.Status);
                entity.HasIndex(t => t.CreatedAt);
                entity.HasIndex(t => new { t.Status, t.IsProcessed });
            });

            // Configuration WebhookLog
            modelBuilder.Entity<WebhookLog>(entity =>
            {
                entity.HasIndex(w => w.TokenPay);
                entity.HasIndex(w => w.ReceivedAt);
                entity.HasIndex(w => w.EventType);
                entity.HasIndex(w => w.IsDuplicate);
            });

            // Configuration ShopifyOrder
            modelBuilder.Entity<ShopifyOrder>(entity =>
            {
                entity.HasIndex(o => o.OrderId).IsUnique();
                entity.HasIndex(o => o.OrderNumber);
                entity.HasIndex(o => o.FinancialStatus);
                entity.HasIndex(o => o.WhatsAppSent);
            });
        }
    }
}
