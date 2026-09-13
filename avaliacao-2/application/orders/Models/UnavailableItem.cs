namespace orders.Models;

public class UnavailableItem
{
    public int ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Requested { get; set; }
    public int Available { get; set; }
}
