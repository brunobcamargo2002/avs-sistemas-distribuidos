using publisher_promotions.Data;
using publisher_promotions.Models;

namespace publisher_promotions.Services;

public static class PromotionGenerator
{
    public static Promotion GenerateRandom()
    {
        var product = ProductCatalog.Products[Random.Shared.Next(ProductCatalog.Products.Count)];
        var discountPercentage = Random.Shared.Next(5, 71);
        var promotionalPrice = Math.Round(product.Price * (1 - discountPercentage / 100m), 2);

        return new Promotion
        {
            Id = Guid.NewGuid(),
            Category = product.Category,
            ProductName = product.Name,
            OriginalPrice = product.Price,
            DiscountPercentage = discountPercentage,
            PromotionalPrice = promotionalPrice,
            Stock = product.Stock,
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }
}
