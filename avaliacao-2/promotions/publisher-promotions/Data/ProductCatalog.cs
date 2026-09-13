using System.Text.Json;
using publisher_promotions.Models;

namespace publisher_promotions.Data;

/// <summary>
/// Carrega o catálogo de produtos (categoria, preço, estoque) a partir do JSON
/// global em avaliacao-2/product-catalog.json, compartilhado entre os microsserviços.
/// </summary>
public static class ProductCatalog
{
    private const string CatalogPath = "../../product-catalog.json";

    public static readonly List<CatalogProduct> Products = Load();

    private static List<CatalogProduct> Load()
    {
        if (!File.Exists(CatalogPath))
        {
            throw new FileNotFoundException($"Catálogo de produtos não encontrado em '{CatalogPath}'.");
        }

        var json = File.ReadAllText(CatalogPath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<List<CatalogProduct>>(json, options) ?? [];
    }
}
