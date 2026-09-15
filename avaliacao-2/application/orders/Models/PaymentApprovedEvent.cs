namespace orders.Models;

public class PaymentApprovedEvent
{
    public Guid OrderId { get; set; }
    public Guid PaymentId { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset ApprovedAt { get; set; }
}
