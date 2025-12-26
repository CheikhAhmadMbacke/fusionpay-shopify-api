using System.Text.Json.Serialization;

namespace FusionPayProxy.Models.Responses
{
    public class PaymentResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("paymentUrl")]
        public string PaymentUrl { get; set; } = string.Empty;

        [JsonPropertyName("returnUrl")]
        public string ReturnUrl { get; set; } = string.Empty;

        [JsonPropertyName("token")]
        public string Token { get; set; } = string.Empty;

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("transactionId")]
        public int TransactionId { get; set; }

        [JsonPropertyName("orderId")]
        public string OrderId { get; set; } = string.Empty;
        [JsonPropertyName("deliveryZone")]
        public string? DeliveryZone { get; set; }
        [JsonPropertyName("deliveryPrice")]
        public decimal DeliveryPrice { get; set; }
        [JsonPropertyName("paymentMethod")]
        public string? PaymentMethod { get; set; } // "cash" ou "mobile"

        [JsonPropertyName("errorMessage")]
        public string? ErrorMessage { get; set; }

        [JsonPropertyName("validationErrors")]
        public List<string>? ValidationErrors { get; set; }

        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("expiresAt")]
        public DateTime? ExpiresAt { get; set; }
    }
}
