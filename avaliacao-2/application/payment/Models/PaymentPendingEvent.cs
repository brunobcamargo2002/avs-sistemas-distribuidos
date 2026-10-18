namespace payment.Models;

/// <summary>
/// Publicado em "pagamento.pendente" assim que o provedor devolve o link de
/// checkout, para o orders exibir ao cliente enquanto aguarda a decisão.
/// </summary>
public class PaymentPendingEvent
{
    public Guid OrderId { get; set; }
    public Guid PaymentId { get; set; }
    public decimal Amount { get; set; }
    public string CheckoutUrl { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
