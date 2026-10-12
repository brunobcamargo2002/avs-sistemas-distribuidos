using System.Net.Http.Json;
using orders.Data;
using orders.Models;
using orders.Services;

namespace orders.Endpoints;

/// <summary>
/// Rotas de pedidos consumidas pelo front (/api/orders).
/// </summary>
public static class OrderEndpoints
{
    public static RouteGroupBuilder MapOrderEndpoints(this IEndpointRouteBuilder app, OrderService orderService)
    {
        var group = app.MapGroup("/api/orders");

        group.MapGet("/", (string customerId) =>
        {
            if (string.IsNullOrWhiteSpace(customerId))
            {
                return Results.BadRequest(new { message = "Informe o identificador do cliente." });
            }

            return Results.Ok(OrderRepository.ListByCustomer(customerId));
        });

        group.MapPost("/", async (CreateOrderRequest request, IHttpClientFactory clients) =>
        {
            if (string.IsNullOrWhiteSpace(request.CustomerId) || request.Items.Count == 0 || request.Items.Any(item => item.Quantity <= 0))
            {
                return Results.BadRequest(new { message = "Informe o cliente e ao menos um item com quantidade válida." });
            }

            List<CatalogProduct> products;
            try
            {
                products = await clients.CreateClient("stock").GetFromJsonAsync<List<CatalogProduct>>("/api/products") ?? [];
            }
            catch (HttpRequestException)
            {
                return Results.Problem("O serviço de estoque não está disponível.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var items = new List<OrderItem>();
            foreach (var item in request.Items.GroupBy(item => item.ProductId).Select(group => new { ProductId = group.Key, Quantity = group.Sum(item => item.Quantity) }))
            {
                var product = products.Find(candidate => candidate.Id == item.ProductId);
                if (product is null)
                {
                    return Results.BadRequest(new { message = $"Produto {item.ProductId} não encontrado." });
                }

                if (product.Stock < item.Quantity)
                {
                    return Results.Conflict(new { message = $"Estoque insuficiente para {product.Name}. Disponível: {product.Stock}." });
                }

                items.Add(new OrderItem { ProductId = product.Id, Name = product.Name, Quantity = item.Quantity });
            }

            var order = await orderService.CreateOrderAsync(request.CustomerId.Trim(), items);
            return Results.Created($"/api/orders?customerId={Uri.EscapeDataString(order.CustomerId)}", new
            {
                order.OrderNumber,
                order.OrderId,
                order.CustomerId,
                order.Items,
                order.Status,
                order.CreatedAt
            });
        });

        return group;
    }
}

public sealed record CreateOrderRequest(string CustomerId, List<CreateOrderItemRequest> Items);
public sealed record CreateOrderItemRequest(int ProductId, int Quantity);
