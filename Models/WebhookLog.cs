using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FusionPayProxy.Models
{
    public class WebhookLog
    {
        public int Id { get; set; }

        [MaxLength(100)]
        public string EventType { get; set; } = string.Empty;

        [MaxLength(100)]
        public string TokenPay { get; set; } = string.Empty;

        [Column(TypeName = "text")]
        public string Payload { get; set; } = string.Empty;

        public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

        public bool IsDuplicate { get; set; } = false;

        [MaxLength(500)]
        public string? ProcessingResult { get; set; }

        [MaxLength(50)]
        public string? IpAddress { get; set; }

        [MaxLength(10)]
        public string? HttpMethod { get; set; }
    }
}
