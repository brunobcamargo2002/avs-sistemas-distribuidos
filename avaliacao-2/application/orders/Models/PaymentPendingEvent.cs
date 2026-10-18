namespace orders.Models;

/// <summary>
/// Recebido em "pagamento.pendente": traz o link de checkout gerado pelo
/// provedor para o cliente concluir o pagamento.
/// </summary>
public class PaymentPendingEvent
{
    public Guid OrderId { get; set; }
    public Guid PaymentId { get; set; }
    public decimal Amount { get; set; }
    public string CheckoutUrl { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
