namespace stock.Models;

public class StockUnavailableEvent
{
    public Guid OrderId { get; set; }
    public List<UnavailableItem> UnavailableItems { get; set; } = [];
    public DateTimeOffset ProcessedAt { get; set; }
}
