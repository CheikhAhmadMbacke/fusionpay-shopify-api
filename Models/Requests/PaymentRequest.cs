using System.ComponentModel.DataAnnotations;

namespace FusionPayProxy.Models.Requests
{
    public class PaymentRequest
    {
        [Required]
        [Range(1, 10000000)]
        public decimal Amount { get; set; }

        [Required]
        [Phone]
        public string CustomerPhone { get; set; } = string.Empty;

        [Required]
        [MinLength(2)]
        public string CustomerName { get; set; } = string.Empty;

        [Required]
        public string OrderId { get; set; } = string.Empty;

        public string OrderNumber { get; set; } = string.Empty;

        [Required]
        [Url]
        public string ReturnUrl { get; set; } = string.Empty;

        public string ProductName { get; set; } = "Commande Afro KingVap";

        public List<OrderItem>? Items { get; set; }
    }

    public class OrderItem
    {
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int Quantity { get; set; } = 1;
    }
}
