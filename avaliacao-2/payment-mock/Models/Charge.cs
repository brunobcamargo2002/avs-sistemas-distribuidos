namespace payment_mock.Models;

/// <summary>
/// Cobrança criada pelo MS Pagamento e persistida em charges.json.
/// </summary>
public class Charge
{
    public Guid ChargeId { get; set; }
    public Guid OrderId { get; set; }
    public decimal Amount { get; set; }
    public string WebhookUrl { get; set; } = string.Empty;
    public string Status { get; set; } = ChargeStatus.Pendente;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
}

public static class ChargeStatus
{
    public const string Pendente = "PENDENTE";
    public const string Aprovado = "APROVADO";
    public const string Recusado = "RECUSADO";
}
