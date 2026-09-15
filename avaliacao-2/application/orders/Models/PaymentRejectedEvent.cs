namespace orders.Models;

public class PaymentRejectedEvent
{
    public Guid OrderId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset AttemptedAt { get; set; }
}
