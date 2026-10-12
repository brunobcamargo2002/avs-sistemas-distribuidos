using orders.Endpoints;
using orders.Services;
using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("GATEWAY_URLS") ?? "http://localhost:5080");
builder.Services.AddHttpClient("stock", client =>
    client.BaseAddress = new Uri(Environment.GetEnvironmentVariable("STOCK_SERVICE_URL") ?? "http://localhost:5081"));

var factory = new ConnectionFactory
{
    HostName = "localhost",
    UserName = "admin",
    Password = "admin"
};

var orderService = await OrderService.CreateAsync(factory);
await orderService.StartConsumingAsync();

var app = builder.Build();
var interfacePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "interface"));
var interfaceProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(interfacePath);

app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = interfaceProvider });
app.UseStaticFiles(new StaticFileOptions { FileProvider = interfaceProvider });

app.MapProductEndpoints();
app.MapOrderEndpoints(orderService);

await app.RunAsync();
