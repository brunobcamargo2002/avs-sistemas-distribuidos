namespace publisher_promotions.Models;

public class Promotion
{
    public Guid Id { get; set; }
    public string Category { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal OriginalPrice { get; set; }
    public int DiscountPercentage { get; set; }
    public decimal PromotionalPrice { get; set; }
    public int Stock { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }

    public string RoutingKey => $"promocao.categoria.{Category}";
}
