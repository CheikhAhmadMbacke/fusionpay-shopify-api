using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FusionPayProxy.Models
{
    public class Transaction
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string ShopifyOrderId { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string ShopifyOrderNumber { get; set; } = string.Empty;

        // Dans Transaction.cs - REMPLACEZ la ligne actuelle par :
        [MaxLength(100)]
        public string? FusionPayToken { get; set; } // ✅ Supprimez [Required] et ajoutez ?

        [MaxLength(50)]
        public string Status { get; set; } = "pending"; // pending, paid, failed, cancelled, no paid

        [Required]
        [MaxLength(20)]
        public string CustomerPhone { get; set; } = string.Empty;

        [MaxLength(200)]
        public string CustomerName { get; set; } = string.Empty;

        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }


        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        public DateTime? PaidAt { get; set; }

        [MaxLength(100)]
        public string? WebhookEvent { get; set; } // payin.session.completed, etc.

        [MaxLength(500)]
        public string? ErrorMessage { get; set; }
        public string? CustomerEmail { get; set; }
        public string? ReturnUrl { get; set; }

        public bool IsProcessed { get; set; } = false;

        public DateTime? ProcessedAt { get; set; }

        [Column(TypeName = "text")]
        public string? RawWebhookData { get; set; }

        [MaxLength(50)]
        public string? TransactionNumber { get; set; } // numeroTransaction

        [Column(TypeName = "decimal(18,2)")]
        public decimal? Fees { get; set; } // frais
        [Column("delivery_zone")]
        public string? DeliveryZone { get; set; }

        [Column("delivery_price")]
        public decimal DeliveryPrice { get; set; }

        [Column("payment_method")]
        public string? PaymentMethod { get; set; }
    }
}
