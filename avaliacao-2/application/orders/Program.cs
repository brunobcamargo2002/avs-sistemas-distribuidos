using System.Net.Http.Json;
using System.Text.Json;
using orders.Data;
using orders.Models;
using orders.Services;
using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("GATEWAY_URLS") ?? "http://localhost:5080");
builder.Services.AddHttpClient();

var factory = new ConnectionFactory
{
    HostName = "localhost",
    UserName = "admin",
    Password = "admin"
};

var orderService = await OrderService.CreateAsync(factory);
await orderService.StartConsumingAsync();

var app = builder.Build();
var stockServiceUrl = Environment.GetEnvironmentVariable("STOCK_SERVICE_URL") ?? "http://localhost:5081";
var interfacePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "interface"));
var interfaceProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(interfacePath);

app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = interfaceProvider });
app.UseStaticFiles(new StaticFileOptions { FileProvider = interfaceProvider });

app.MapGet("/api/products", async (IHttpClientFactory clients) =>
{
    try
    {
        var products = await clients.CreateClient().GetFromJsonAsync<List<CatalogProduct>>($"{stockServiceUrl}/api/products");
        return Results.Ok(products ?? []);
    }
    catch (HttpRequestException)
    {
        return Results.Problem("O serviço de estoque não está disponível.", statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapGet("/api/orders", (string customerId) =>
{
    if (string.IsNullOrWhiteSpace(customerId))
    {
        return Results.BadRequest(new { message = "Informe o identificador do cliente." });
    }

    return Results.Ok(OrderRepository.ListByCustomer(customerId));
});

app.MapPost("/api/orders", async (CreateOrderRequest request, IHttpClientFactory clients) =>
{
    if (string.IsNullOrWhiteSpace(request.CustomerId) || request.Items.Count == 0 || request.Items.Any(item => item.Quantity <= 0))
    {
        return Results.BadRequest(new { message = "Informe o cliente e ao menos um item com quantidade válida." });
    }

    List<CatalogProduct> products;
    try
    {
        products = await clients.CreateClient().GetFromJsonAsync<List<CatalogProduct>>($"{stockServiceUrl}/api/products") ?? [];
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

await app.RunAsync();

public sealed record CreateOrderRequest(string CustomerId, List<CreateOrderItemRequest> Items);
public sealed record CreateOrderItemRequest(int ProductId, int Quantity);
