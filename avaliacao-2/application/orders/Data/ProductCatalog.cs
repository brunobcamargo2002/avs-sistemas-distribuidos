using System.Text.Json;
using orders.Models;

namespace orders.Data;

/// <summary>
/// Leitura do catálogo de produtos global (avaliacao-2/product-catalog.json).
/// O orders só consulta o estoque para exibição; quem dá baixa/repõe é o stock.
/// </summary>
public static class ProductCatalog
{
    private const string CatalogPath = "../../product-catalog.json";
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    public static List<CatalogProduct> List()
    {
        if (!File.Exists(CatalogPath))
        {
            throw new FileNotFoundException($"Catálogo de produtos não encontrado em '{CatalogPath}'.");
        }

        var json = File.ReadAllText(CatalogPath);
        return JsonSerializer.Deserialize<List<CatalogProduct>>(json, ReadOptions) ?? [];
    }
}
