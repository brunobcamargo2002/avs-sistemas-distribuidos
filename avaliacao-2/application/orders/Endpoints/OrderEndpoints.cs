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

        // O pedido é apenas registrado e publicado em "pedido.criado": quem decide
        // sobre disponibilidade é o ms-estoque, de forma assíncrona.
        group.MapPost("/", async (CreateOrderRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.CustomerId))
            {
                return Results.BadRequest(new { message = "Informe o identificador do cliente." });
            }

            var items = request.Items
                .Select(item => new OrderItem { ProductId = item.ProductId, Name = item.Name, Quantity = item.Quantity })
                .ToList();

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
public sealed record CreateOrderItemRequest(int ProductId, string Name, int Quantity);
