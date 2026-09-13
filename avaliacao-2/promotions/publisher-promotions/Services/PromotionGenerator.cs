using publisher_promotions.Data;
using publisher_promotions.Models;

namespace publisher_promotions.Services;

public static class PromotionGenerator
{
    public static Promotion GenerateRandom()
    {
        var (category, name) = ProductCatalog.Products[Random.Shared.Next(ProductCatalog.Products.Length)];
        var discountPercentage = Random.Shared.Next(5, 71);
        var originalPrice = Math.Round(Random.Shared.Next(5000, 1200001) / 100m, 2);
        var promotionalPrice = Math.Round(originalPrice * (1 - discountPercentage / 100m), 2);

        return new Promotion
        {
            Id = Guid.NewGuid(),
            Category = category,
            ProductName = name,
            OriginalPrice = originalPrice,
            DiscountPercentage = discountPercentage,
            PromotionalPrice = promotionalPrice,
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }
}
