namespace orders.Models;

public class StockOkEvent
{
    public Guid OrderId { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }
}
