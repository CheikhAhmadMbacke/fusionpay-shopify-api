namespace FusionPayProxy.Models.Requests
{
    public class FusionPayWebhookRequest
    {
        public string Event { get; set; } = string.Empty;
        public List<PersonalInfo> Personal_Info { get; set; } = new List<PersonalInfo>();
        public string TokenPay { get; set; } = string.Empty;
        public string NumeroSend { get; set; } = string.Empty;
        public string Nomclient { get; set; } = string.Empty;
        public decimal Montant { get; set; }
        public decimal Frais { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class PersonalInfo
    {
        public string? OrderId { get; set; }
    }
}
