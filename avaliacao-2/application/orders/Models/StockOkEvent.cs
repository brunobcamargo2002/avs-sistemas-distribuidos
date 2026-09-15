namespace orders.Models;

public class StockOkEvent
{
    public Guid OrderId { get; set; }
    public List<OrderItem> Items { get; set; } = [];
    public decimal Total { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }
}
