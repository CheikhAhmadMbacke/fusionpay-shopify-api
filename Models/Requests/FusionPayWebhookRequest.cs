// Dans Models/Requests/FusionPayWebhookRequest.cs
using System.Text.Json;
using System.Text.Json.Serialization;

public class FusionPayWebhookRequest
{
    [JsonPropertyName("event")]
    public string Event { get; set; } = string.Empty;

    [JsonPropertyName("tokenPay")]
    public string TokenPay { get; set; } = string.Empty; // FusionPay peut envoyer string OU number

    [JsonPropertyName("numeroSend")]
    public string NumeroSend { get; set; } = string.Empty;

    [JsonPropertyName("nomclient")]
    public string NomClient { get; set; } = string.Empty;

    [JsonPropertyName("numeroTransaction")]
    public string NumeroTransaction { get; set; } = string.Empty;

    [JsonPropertyName("Montant")]
    public decimal Montant { get; set; }

    [JsonPropertyName("frais")]
    public decimal Frais { get; set; }

    [JsonPropertyName("personal_Info")]
    public JsonElement PersonalInfo { get; set; } // Ou créez un modèle spécifique

    [JsonPropertyName("return_url")]
    public string ReturnUrl { get; set; } = string.Empty;

    [JsonPropertyName("webhook_url")]
    public string WebhookUrl { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }
}
