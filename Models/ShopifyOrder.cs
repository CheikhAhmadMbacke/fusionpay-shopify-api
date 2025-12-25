using System.ComponentModel.DataAnnotations;

namespace FusionPayProxy.Models
{
    public class ShopifyOrder
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string OrderId { get; set; } = string.Empty;

        [MaxLength(20)]
        public string? OrderNumber { get; set; }

        [MaxLength(100)]
        public string? CustomerName { get; set; }

        [MaxLength(20)]
        public string? CustomerPhone { get; set; }

        [EmailAddress]
        [MaxLength(100)]
        public string? CustomerEmail { get; set; } // CHANGÉ EN NULLABLE

        [Required]
        public decimal TotalPrice { get; set; }

        public string Currency { get; set; } = "XOF";
        public string FinancialStatus { get; set; } = "pending";
        public string FulfillmentStatus { get; set; } = "unfulfilled";
        public string? OrderData { get; set; }
        public bool WhatsAppSent { get; set; }
        public DateTime? WhatsAppSentAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
