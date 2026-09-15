namespace payment.Models;

public class StockOkEvent
{
    public Guid OrderId { get; set; }
    public decimal Total { get; set; }
}
