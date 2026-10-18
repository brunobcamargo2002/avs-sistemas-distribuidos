using payment.Models;
using payment.Services;

namespace payment.Endpoints;

/// <summary>
/// Endpoint HTTP do MS Pagamento: o webhook de retorno do provedor.
/// </summary>
public static class PaymentEndpoints
{
    public static RouteGroupBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app, PaymentService payments)
    {
        var group = app.MapGroup("/api");

        // Chamado pelo mock quando o cliente decide no checkout. Vem de um sistema
        // externo, portanto sem assinatura RSA.
        group.MapPost("/webhooks/payment", async (WebhookNotification notification) =>
            await payments.ConfirmAsync(notification)
                ? Results.NoContent()
                : Results.BadRequest(new { message = "Informe orderId e o status APROVADO ou RECUSADO." }));

        return group;
    }
}
