using System.Net.Http.Json;
using payment_mock.Data;
using payment_mock.Models;

namespace payment_mock.Endpoints;

/// <summary>
/// API REST do provedor: criação da cobrança pelo MS Pagamento e a decisão
/// disparada pelos botões da página de checkout.
/// </summary>
public static class ChargeEndpoints
{
    public static RouteGroupBuilder MapChargeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/charges");

        // Chamada síncrona do MS Pagamento: cria a cobrança e devolve o link.
        group.MapPost("/", (CreateChargeRequest request, IConfiguration config) =>
        {
            if (request.OrderId == Guid.Empty || string.IsNullOrWhiteSpace(request.WebhookUrl))
            {
                return Results.BadRequest(new { message = "Informe orderId e webhookUrl." });
            }

            var charge = new Charge
            {
                ChargeId = Guid.NewGuid(),
                OrderId = request.OrderId,
                Amount = request.Amount,
                WebhookUrl = request.WebhookUrl.Trim(),
                Status = ChargeStatus.Pendente,
                CreatedAt = DateTimeOffset.UtcNow
            };

            ChargeStore.Add(charge);
            Console.WriteLine($" [x] Cobrança {charge.ChargeId} criada para o pedido {charge.OrderId} ({charge.Amount:C2}).");

            return Results.Ok(new ChargeCreatedResponse(charge.ChargeId, $"{PublicUrl(config)}/checkout/{charge.ChargeId}"));
        });

        // Clique nos botões da página: decide a cobrança e dispara o webhook.
        group.MapPost("/{chargeId:guid}/decide", async (Guid chargeId, DecisionRequest request, IHttpClientFactory clients) =>
        {
            var status = request.Status?.Trim().ToUpperInvariant();
            if (status is not (ChargeStatus.Aprovado or ChargeStatus.Recusado))
            {
                return Results.BadRequest(new { message = "Status deve ser APROVADO ou RECUSADO." });
            }

            if (ChargeStore.Find(chargeId) is null)
            {
                return Results.NotFound();
            }

            var charge = ChargeStore.TryDecide(chargeId, status);
            if (charge is null)
            {
                return Results.Conflict(new { message = "Cobrança já foi decidida." });
            }

            var notification = new WebhookNotification(charge.ChargeId, charge.OrderId, charge.Status, charge.Amount, charge.DecidedAt!.Value);

            try
            {
                using var response = await clients.CreateClient().PostAsJsonAsync(charge.WebhookUrl, notification);
                if (!response.IsSuccessStatusCode)
                {
                    ChargeStore.Reopen(chargeId);
                    Console.WriteLine($" [!] Webhook recusado pelo lojista ({(int)response.StatusCode}) para a cobrança {chargeId}.");
                    return Results.Problem($"O lojista respondeu {(int)response.StatusCode} ao webhook.", statusCode: StatusCodes.Status502BadGateway);
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                ChargeStore.Reopen(chargeId);
                Console.WriteLine($" [!] Webhook não entregue para {charge.WebhookUrl}: {exception.Message}");
                return Results.Problem("Não foi possível notificar o lojista. Verifique se o MS Pagamento está no ar e tente de novo.",
                    statusCode: StatusCodes.Status502BadGateway);
            }

            Console.WriteLine($" [x] Cobrança {chargeId} -> {status}. Webhook entregue em {charge.WebhookUrl}.");
            return Results.Ok(notification);
        });

        return group;
    }

    /// <summary>URL usada para montar o link devolvido ao MS Pagamento.</summary>
    private static string PublicUrl(IConfiguration config) =>
        (config["PAYMENT_MOCK_PUBLIC_URL"] ?? "http://localhost:5083").TrimEnd('/');
}
