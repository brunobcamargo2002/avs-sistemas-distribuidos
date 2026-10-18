namespace payment_mock.Models;

/// <summary>Corpo enviado pelo MS Pagamento em POST /api/charges.</summary>
public sealed record CreateChargeRequest(Guid OrderId, decimal Amount, string WebhookUrl);

/// <summary>Resposta síncrona com o link de checkout gerado.</summary>
public sealed record ChargeCreatedResponse(Guid ChargeId, string CheckoutUrl);

/// <summary>Corpo enviado pela página de checkout ao clicar nos botões.</summary>
public sealed record DecisionRequest(string Status);

/// <summary>Corpo do webhook disparado para a URL informada pelo MS Pagamento.</summary>
public sealed record WebhookNotification(Guid ChargeId, Guid OrderId, string Status, DateTimeOffset DecidedAt);
