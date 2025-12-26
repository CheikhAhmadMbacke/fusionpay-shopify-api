using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace FusionPayProxy.Models.Requests
{
    public class PaymentRequest
    {
        [Required(ErrorMessage = "L'ID de commande est requis")]
        [StringLength(50, MinimumLength = 3, ErrorMessage = "L'ID de commande doit contenir entre 3 et 50 caractères")]
        [JsonPropertyName("orderId")]
        public string OrderId { get; set; } = string.Empty;

        [StringLength(20, ErrorMessage = "Le numéro de commande est trop long")]
        [JsonPropertyName("orderNumber")]
        public string? OrderNumber { get; set; }

        [Required(ErrorMessage = "Le numéro de téléphone du client est requis")]
        [Phone(ErrorMessage = "Numéro de téléphone invalide")]
        [RegularExpression(@"^[0-9]{8,15}$", ErrorMessage = "Numéro de téléphone invalide (8-15 chiffres)")]
        [JsonPropertyName("customerPhone")]
        public string CustomerPhone { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le nom du client est requis")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "Le nom doit contenir entre 2 et 100 caractères")]
        [JsonPropertyName("customerName")]
        public string CustomerName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le montant est requis")]
        [Range(201, 1000000, ErrorMessage = "Le montant doit être supérieur à 200 FCFA")]
        [JsonPropertyName("amount")]
        public decimal Amount { get; set; }

        [EmailAddress(ErrorMessage = "Email invalide")]
        [JsonPropertyName("customerEmail")]
        public string? CustomerEmail { get; set; }
        [JsonPropertyName("deliveryZone")]
        public string? DeliveryZone { get; set; }

        [JsonPropertyName("deliveryPrice")]
        public decimal DeliveryPrice { get; set; }

        [JsonPropertyName("paymentMethod")]
        public string? PaymentMethod { get; set; } // "cash" ou "mobile"

        // Articles dynamiques depuis Shopify
        [JsonPropertyName("articles")]
        public List<ShopifyArticle>? Articles { get; set; }

        // Classe pour les articles Shopify
        public class ShopifyArticle
        {
            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("price")]
            public decimal Price { get; set; }

            [JsonPropertyName("quantity")]
            public int Quantity { get; set; } = 1;
        }

        // ========== MÉTHODES FUSIONPAY ==========

        /// <summary>
        /// Génère le format d'article selon la documentation FusionPay
        /// Format: [{"nom_du_produit": prix}, {"autre_produit": prix}]
        /// </summary>
        public object GetFusionPayArticleFormat()
        {
            var fusionPayArticles = new List<Dictionary<string, int>>();

            // Si on a des articles spécifiques de Shopify
            if (Articles != null && Articles.Any())
            {
                foreach (var article in Articles)
                {
                    // Format clé-valeur selon documentation FusionPay
                    // Ex: {"Vape Pen X": 2500}, {"E-Liquid Mint": 3000}
                    var articleDict = new Dictionary<string, int>
                    {
                        { article.Name, (int)(article.Price * article.Quantity) }
                    };
                    fusionPayArticles.Add(articleDict);
                }
            }
            else
            {
                // Format par défaut (commande unique)
                fusionPayArticles.Add(new Dictionary<string, int>
                {
                    { "Commande AfroKingVap", (int)Amount }
                });
            }

            return fusionPayArticles;
        }

        /// <summary>
        /// Génère le format personal_Info selon la documentation FusionPay
        /// Format: [{"userId": 1, "orderId": "123"}]
        /// </summary>
        public object GetFusionPayPersonalInfo(int transactionId)
        {
            return new[]
            {
                new
                {
                    userId = 1, // ID utilisateur (à adapter selon votre logique)
                    orderId = OrderId,
                    transactionId = transactionId,
                    shop = "AfroKingVap",
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                }
            };
        }

        /// <summary>
        /// Génère l'URL de remerciement avec les paramètres
        /// </summary>
        public string GenerateThankYouUrl(string token, string baseUrl)
        {
            var parameters = new Dictionary<string, string>
            {
                ["orderId"] = OrderId,
                ["token"] = token,
                ["amount"] = Amount.ToString(),
                ["customer"] = Uri.EscapeDataString(CustomerName),
                ["phone"] = CustomerPhone,
                ["paymentMethod"] = PaymentMethod ?? "mobile",
                ["timestamp"] = DateTime.UtcNow.ToString("yyyyMMddHHmmss")
            };

            // Ajouter les paramètres de livraison si disponibles
            if (!string.IsNullOrEmpty(DeliveryZone))
            {
                parameters["deliveryZone"] = Uri.EscapeDataString(DeliveryZone);
            }

            parameters["deliveryPrice"] = DeliveryPrice.ToString();

            // Construire l'URL
            var queryString = string.Join("&", parameters.Select(p => $"{p.Key}={p.Value}"));
            return $"{baseUrl}?{queryString}";
        }

        /// <summary>
        /// Valide les données de la requête
        /// </summary>
        public bool Validate(out List<string> errors)
        {
            errors = new List<string>();

            if (Amount <= 200)
                errors.Add("Le montant doit être supérieur à 200 FCFA");

            if (string.IsNullOrWhiteSpace(CustomerPhone) || CustomerPhone.Length < 8)
                errors.Add("Numéro de téléphone invalide");

            if (string.IsNullOrWhiteSpace(CustomerName) || CustomerName.Length < 2)
                errors.Add("Nom du client invalide");

            if (string.IsNullOrWhiteSpace(OrderId))
                errors.Add("ID de commande requis");

            return errors.Count == 0;
        }

        /// <summary>
        /// Formate le numéro de téléphone pour FusionPay (chiffres uniquement)
        /// </summary>
        public string GetFormattedPhone()
        {
            return new string(CustomerPhone.Where(char.IsDigit).ToArray());
        }


    }
}
