using RabbitMQ.Client;
using stock.Services;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("STOCK_URLS") ?? "http://localhost:5081");

var factory = new ConnectionFactory
{
    HostName = "localhost",
    UserName = "admin",
    Password = "admin"
};

var stockService = await StockService.CreateAsync(factory);
await stockService.StartConsumingAsync();

var app = builder.Build();
app.MapGet("/api/products", () => stock.Data.ProductCatalog.ListProducts());

await app.RunAsync();
