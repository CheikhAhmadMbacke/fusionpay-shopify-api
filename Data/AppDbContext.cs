using FusionPayProxy.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FusionPayProxy.Data
{
    public class AppDbContext : DbContext
    {
        private readonly ILogger<AppDbContext> _logger;

        public AppDbContext(
            DbContextOptions<AppDbContext> options,
            ILogger<AppDbContext> logger = null)
            : base(options)
        {
            _logger = logger;

            // ✅ FORCER LA CRÉATION/APPLICATION DES MIGRATIONS AU DÉMARRAGE
            InitializeDatabase();
        }

        public DbSet<Transaction> Transactions { get; set; }
        public DbSet<WebhookLog> WebhookLogs { get; set; }
        public DbSet<ShopifyOrder> ShopifyOrders { get; set; }

        private void InitializeDatabase()
        {
            try
            {
                // Vérifier si la base de données existe déjà
                var databaseExists = Database.CanConnect();

                if (!databaseExists)
                {
                    _logger?.LogInformation("📦 Database doesn't exist, creating...");

                    // Créer la base de données et les tables
                    Database.EnsureCreated();

                    _logger?.LogInformation("✅ Database created successfully");
                }
                else
                {
                    // Vérifier si les tables existent
                    var tablesExist = CheckIfTablesExist();

                    if (!tablesExist)
                    {
                        _logger?.LogInformation("📝 Tables missing, creating...");
                        Database.EnsureCreated();
                        _logger?.LogInformation("✅ Tables created successfully");
                    }
                    else
                    {
                        _logger?.LogInformation("🔄 Database and tables already exist");
                    }

                    // Toujours essayer d'appliquer les migrations
                    TryApplyMigrations();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "❌ Error initializing database");

                // En cas d'erreur, essayer de créer la base de toute façon
                try
                {
                    Database.EnsureCreated();
                    _logger?.LogWarning("⚠️ Database created after initial error");
                }
                catch (Exception fallbackEx)
                {
                    _logger?.LogCritical(fallbackEx, "💥 Critical: Could not create database");
                }
            }
        }

        private bool CheckIfTablesExist()
        {
            try
            {
                // Vérifier si la table Transactions existe
                var sql = @"
                    SELECT COUNT(*) FROM sqlite_master 
                    WHERE type='table' AND name IN ('Transactions', 'ShopifyOrders', 'WebhookLogs')";

                var tableCount = Database.ExecuteSqlRaw(sql);
                return tableCount == 3; // Les 3 tables doivent exister
            }
            catch
            {
                return false;
            }
        }

        private void TryApplyMigrations()
        {
            try
            {
                // Vérifier s'il y a des migrations en attente
                var pendingMigrations = Database.GetPendingMigrations();

                if (pendingMigrations.Any())
                {
                    _logger?.LogInformation($"🔄 Applying {pendingMigrations.Count()} pending migrations...");
                    Database.Migrate();
                    _logger?.LogInformation("✅ Migrations applied successfully");
                }
                else
                {
                    _logger?.LogDebug("✅ No pending migrations");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "⚠️ Could not apply migrations, continuing with current schema");
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configuration Transaction
            modelBuilder.Entity<Transaction>(entity =>
            {
                entity.HasKey(e => e.Id);
                //entity.HasIndex(t => t.FusionPayToken).IsUnique();
                entity.HasIndex(t => t.ShopifyOrderId);
                entity.HasIndex(t => t.Status);
                entity.HasIndex(t => t.CreatedAt);
                entity.HasIndex(t => new { t.Status, t.IsProcessed });

                // Configurations supplémentaires pour la stabilité
                entity.Property(t => t.CreatedAt).HasDefaultValueSql("datetime('now')");
                entity.Property(t => t.UpdatedAt).HasDefaultValueSql("datetime('now')");
                entity.Property(t => t.Status).HasDefaultValue("pending");
                entity.Property(t => t.IsProcessed).HasDefaultValue(false);

                // Configuration des colonnes décimales
                entity.Property(t => t.Amount).HasPrecision(18, 2);
                entity.Property(t => t.Fees).HasPrecision(18, 2);
            });

            // Configuration WebhookLog
            modelBuilder.Entity<WebhookLog>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(w => w.TokenPay);
                entity.HasIndex(w => w.ReceivedAt);
                entity.HasIndex(w => w.EventType);
                entity.HasIndex(w => w.IsDuplicate);

                entity.Property(w => w.ReceivedAt).HasDefaultValueSql("datetime('now')");
                entity.Property(w => w.IsDuplicate).HasDefaultValue(false);
            });

            // Configuration ShopifyOrder
            modelBuilder.Entity<ShopifyOrder>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(o => o.OrderId).IsUnique();
                entity.HasIndex(o => o.OrderNumber);
                entity.HasIndex(o => o.FinancialStatus);
                entity.HasIndex(o => o.WhatsAppSent);

                entity.Property(o => o.CreatedAt).HasDefaultValueSql("datetime('now')");
                entity.Property(o => o.UpdatedAt).HasDefaultValueSql("datetime('now')");
                entity.Property(o => o.FinancialStatus).HasDefaultValue("pending");
                entity.Property(o => o.FulfillmentStatus).HasDefaultValue("unfulfilled");
                entity.Property(o => o.Currency).HasDefaultValue("XOF");
                entity.Property(o => o.WhatsAppSent).HasDefaultValue(false);
                entity.Property(o => o.TotalPrice).HasPrecision(18, 2);
            });
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            // ✅ CORRECTION : Mettre à jour automatiquement les dates UpdatedAt
            // pour toutes les entités qui ont ces propriétés
            var entries = ChangeTracker.Entries()
                .Where(e => e.State == EntityState.Modified || e.State == EntityState.Added);

            foreach (var entry in entries)
            {
                // Pour Transaction
                if (entry.Entity is Transaction transaction)
                {
                    transaction.UpdatedAt = DateTime.UtcNow;

                    if (entry.State == EntityState.Added && transaction.CreatedAt == default)
                    {
                        transaction.CreatedAt = DateTime.UtcNow;
                    }
                }

                // Pour ShopifyOrder
                if (entry.Entity is ShopifyOrder shopifyOrder)
                {
                    shopifyOrder.UpdatedAt = DateTime.UtcNow;

                    if (entry.State == EntityState.Added && shopifyOrder.CreatedAt == default)
                    {
                        shopifyOrder.CreatedAt = DateTime.UtcNow;
                    }
                }

                // Pour WebhookLog
                if (entry.Entity is WebhookLog webhookLog && entry.State == EntityState.Added)
                {
                    if (webhookLog.ReceivedAt == default)
                    {
                        webhookLog.ReceivedAt = DateTime.UtcNow;
                    }
                }
            }

            return base.SaveChangesAsync(cancellationToken);
        }

        public override int SaveChanges()
        {
            // Version synchrone
            UpdateTimestamps();
            return base.SaveChanges();
        }

        private void UpdateTimestamps()
        {
            var entries = ChangeTracker.Entries()
                .Where(e => e.State == EntityState.Modified || e.State == EntityState.Added);

            foreach (var entry in entries)
            {
                // Pour Transaction
                if (entry.Entity is Transaction transaction)
                {
                    transaction.UpdatedAt = DateTime.UtcNow;

                    if (entry.State == EntityState.Added && transaction.CreatedAt == default)
                    {
                        transaction.CreatedAt = DateTime.UtcNow;
                    }
                }

                // Pour ShopifyOrder
                if (entry.Entity is ShopifyOrder shopifyOrder)
                {
                    shopifyOrder.UpdatedAt = DateTime.UtcNow;

                    if (entry.State == EntityState.Added && shopifyOrder.CreatedAt == default)
                    {
                        shopifyOrder.CreatedAt = DateTime.UtcNow;
                    }
                }
            }
        }
    }
}
