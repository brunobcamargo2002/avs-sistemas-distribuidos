using System.Net.Http.Json;
using orders.Models;

namespace orders.Endpoints;

/// <summary>
/// Repassa ao front o catálogo publicado pela API do ms-estoque.
/// </summary>
public static class ProductEndpoints
{
    public static RouteGroupBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/products");

        group.MapGet("/", async (IHttpClientFactory clients) =>
        {
            try
            {
                var products = await clients.CreateClient("stock").GetFromJsonAsync<List<CatalogProduct>>("/api/products");
                return Results.Ok(products ?? []);
            }
            catch (HttpRequestException)
            {
                return Results.Problem("O serviço de estoque não está disponível.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });

        return group;
    }
}
