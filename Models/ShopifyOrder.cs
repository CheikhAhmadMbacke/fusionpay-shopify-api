using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FusionPayProxy.Models
{
    public class ShopifyOrder
    {
        public int Id { get; set; }

        [MaxLength(100)]
        public string OrderId { get; set; } = string.Empty;

        [MaxLength(100)]
        public string OrderNumber { get; set; } = string.Empty;

        [MaxLength(50)]
        public string FinancialStatus { get; set; } = string.Empty; // pending, paid, refunded

        [MaxLength(50)]
        public string FulfillmentStatus { get; set; } = string.Empty;

        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalPrice { get; set; }

        [MaxLength(20)]
        public string Currency { get; set; } = "XOF";

        [MaxLength(100)]
        public string CustomerEmail { get; set; } = string.Empty;

        [MaxLength(50)]
        public string CustomerPhone { get; set; } = string.Empty;

        [MaxLength(200)]
        public string CustomerName { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        [Column(TypeName = "text")]
        public string? OrderData { get; set; } // JSON complet de la commande

        public bool WhatsAppSent { get; set; } = false;

        public DateTime? WhatsAppSentAt { get; set; }
    }
}
