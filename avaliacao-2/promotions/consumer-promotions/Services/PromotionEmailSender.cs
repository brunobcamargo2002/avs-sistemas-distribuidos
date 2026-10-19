using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using consumer_promotions.Data;
using consumer_promotions.Models;

namespace consumer_promotions.Services;

public sealed class PromotionEmailSender
{
    private static readonly CultureInfo PriceCulture = CultureInfo.GetCultureInfo("pt-BR");
    private readonly HttpClient client;
    private readonly string apiKey;
    private readonly string from;
    private readonly PromotionDeliveryRepository deliveries;
    private readonly SemaphoreSlim sendLock = new(1, 1);

    public PromotionEmailSender(HttpClient client, string apiKey, string from, PromotionDeliveryRepository deliveries)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Configure RESEND_API_KEY before starting the promotion service.");
        }
        if (string.IsNullOrWhiteSpace(from) || !MailAddress.TryCreate(from, out _))
        {
            throw new InvalidOperationException("Configure PROMOTIONS_FROM_EMAIL with an authorized Resend sender.");
        }

        this.client = client;
        this.apiKey = apiKey.Trim();
        this.from = from.Trim();
        this.deliveries = deliveries;
    }

    public async Task<bool> SendAsync(Promotion promotion, string email, CancellationToken cancellationToken = default)
    {
        var recipient = email.Trim().ToLowerInvariant();
        var recipientHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(recipient)));
        var deliveryKey = $"promotion/{promotion.Id:N}/{recipientHash}";
        await sendLock.WaitAsync(cancellationToken);
        try
        {
            if (deliveries.WasSent(deliveryKey))
            {
                return false;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Headers.Add("Idempotency-Key", deliveryKey);
            request.Content = JsonContent.Create(new
            {
                from,
                to = new[] { recipient },
                subject = $"Oferta: {promotion.ProductName} - {promotion.DiscountPercentage}% OFF",
                text = $"{promotion.ProductName}\nCategoria: {promotion.Category}\n" +
                    $"Desconto: {promotion.DiscountPercentage}%\n" +
                    $"Preco original: {promotion.OriginalPrice.ToString("C", PriceCulture)}\n" +
                    $"Preco promocional: {promotion.PromotionalPrice.ToString("C", PriceCulture)}\n\n" +
                    "Para cancelar suas inscricoes, acesse a area de interesses da loja."
            });

            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Email provider returned HTTP {(int)response.StatusCode}.",
                    null, response.StatusCode);
            }

            try
            {
                using var result = await JsonDocument.ParseAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
                if (result.RootElement.ValueKind != JsonValueKind.Object ||
                    !result.RootElement.TryGetProperty("id", out var id) ||
                    id.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(id.GetString()))
                {
                    throw new HttpRequestException("Email provider returned no email ID.");
                }
            }
            catch (JsonException)
            {
                throw new HttpRequestException("Email provider returned an invalid response.");
            }

            deliveries.MarkSent(deliveryKey);
            return true;
        }
        finally
        {
            sendLock.Release();
        }
    }
}