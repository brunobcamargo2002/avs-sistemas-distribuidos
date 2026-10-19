using orders.Endpoints;
using orders.Services;
using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("GATEWAY_URLS") ?? "http://localhost:5080");
builder.Services.AddHttpClient("stock", client =>
    client.BaseAddress = new Uri(Environment.GetEnvironmentVariable("STOCK_SERVICE_URL") ?? "http://localhost:5081"));

// O front tem servidor próprio, então toda chamada do navegador é cross-origin.
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins((Environment.GetEnvironmentVariable("FRONTEND_ORIGIN") ?? "http://localhost:5173").Split(';'))
    .AllowAnyHeader()
    .AllowAnyMethod()));

var factory = new ConnectionFactory
{
    HostName = "localhost",
    UserName = "admin",
    Password = "admin"
};

var notifier = new OrderNotifier();
var orderService = await OrderService.CreateAsync(factory, notifier);
await orderService.StartConsumingAsync();

var app = builder.Build();

app.UseCors();

app.MapProductEndpoints();
app.MapOrderEndpoints(orderService, notifier);
app.MapPromotionInterestEndpoints(orderService);

await app.RunAsync();
