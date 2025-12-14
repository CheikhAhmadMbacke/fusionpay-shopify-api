using FusionPayProxy.Models;
using FusionPayProxy.Models.Requests;
using FusionPayProxy.Models.Responses;

namespace FusionPayProxy.Services
{
    public interface IFusionPayService
    {
        Task<PaymentResponse> InitiatePaymentAsync(PaymentRequest request);
        Task<bool> HandleWebhookAsync(FusionPayWebhookRequest webhook);
        Task<string> VerifyPaymentStatusAsync(string token);
        Task<Transaction?> GetTransactionByTokenAsync(string token);
        Task<List<Transaction>> GetPendingTransactionsAsync();
    }
}
