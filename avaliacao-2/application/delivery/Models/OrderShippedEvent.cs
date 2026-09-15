namespace delivery.Models;

public class OrderShippedEvent
{
    public Guid OrderId { get; set; }
    public Guid InvoiceId { get; set; }
    public string TrackingCode { get; set; } = string.Empty;
    public DateTimeOffset ShippedAt { get; set; }
}
