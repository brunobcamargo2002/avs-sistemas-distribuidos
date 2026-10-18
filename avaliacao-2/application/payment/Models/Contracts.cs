namespace payment.Models;

/// <summary>Corpo enviado ao mock em POST /api/charges.</summary>
public sealed record CreateChargeRequest(Guid OrderId, decimal Amount, string WebhookUrl);

/// <summary>Resposta síncrona do mock com o link de checkout.</summary>
public sealed record ChargeCreatedResponse(Guid ChargeId, string CheckoutUrl);

/// <summary>Corpo do webhook recebido do mock quando o cliente decide.</summary>
public sealed record WebhookNotification(Guid ChargeId, Guid OrderId, string Status, decimal Amount, DateTimeOffset DecidedAt);
