using System.Text.Encodings.Web;
using System.Text.Json;
using stock.Models;

namespace stock.Data;

/// <summary>
/// Lê e grava o catálogo de produtos (categoria, preço, estoque) diretamente no
/// JSON global em avaliacao-2/product-catalog.json a cada operação — o arquivo
/// é a fonte da verdade, sem estado em memória entre chamadas.
/// </summary>
public static class ProductCatalog
{
    private const string CatalogPath = "../../product-catalog.json";
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static List<UnavailableItem> GetUnavailableItems(IEnumerable<OrderItem> items)
    {
        var products = ReadProducts();
        var unavailable = new List<UnavailableItem>();

        foreach (var item in items)
        {
            var available = Find(products, item.ProductId)?.Stock ?? 0;

            if (available < item.Quantity)
            {
                unavailable.Add(new UnavailableItem
                {
                    ProductId = item.ProductId,
                    Name = item.Name,
                    Requested = item.Quantity,
                    Available = available
                });
            }
        }

        return unavailable;
    }

    public static void Reserve(IEnumerable<OrderItem> items)
    {
        var products = ReadProducts();

        foreach (var item in items)
        {
            var product = Find(products, item.ProductId);
            if (product is not null)
            {
                product.Stock -= item.Quantity;
            }
        }

        WriteProducts(products);
    }

    public static void Release(IEnumerable<OrderItem> items)
    {
        var products = ReadProducts();

        foreach (var item in items)
        {
            var product = Find(products, item.ProductId);
            if (product is not null)
            {
                product.Stock += item.Quantity;
            }
        }

        WriteProducts(products);
    }

    private static CatalogProduct? Find(List<CatalogProduct> products, int productId)
    {
        return products.Find(p => p.Id == productId);
    }

    private static List<CatalogProduct> ReadProducts()
    {
        if (!File.Exists(CatalogPath))
        {
            throw new FileNotFoundException($"Catálogo de produtos não encontrado em '{CatalogPath}'.");
        }

        var json = File.ReadAllText(CatalogPath);
        return JsonSerializer.Deserialize<List<CatalogProduct>>(json, ReadOptions) ?? [];
    }

    private static void WriteProducts(List<CatalogProduct> products)
    {
        File.WriteAllText(CatalogPath, JsonSerializer.Serialize(products, WriteOptions));
    }
}
