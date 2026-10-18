using payment.Endpoints;
using payment.Services;
using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("PAYMENT_URLS") ?? "http://localhost:5082");

var factory = new ConnectionFactory
{
    HostName = "localhost",
    UserName = "admin",
    Password = "admin"
};

// Provedor externo de pagamentos (mock) consultado de forma síncrona.
var provider = new HttpClient
{
    BaseAddress = new Uri(Environment.GetEnvironmentVariable("PAYMENT_MOCK_URL") ?? "http://localhost:5083"),
    Timeout = TimeSpan.FromSeconds(10)
};

// URL informada ao provedor para o retorno do webhook.
var webhookUrl = Environment.GetEnvironmentVariable("PAYMENT_WEBHOOK_URL") ?? "http://localhost:5082/api/webhooks/payment";

var service = await PaymentService.CreateAsync(factory, provider, webhookUrl);
await service.StartConsumingAsync();

var app = builder.Build();
app.MapPaymentEndpoints(service);

Console.WriteLine($"Serviço de Pagamento iniciado. Webhook em {webhookUrl}; provedor em {provider.BaseAddress}.");
await app.RunAsync();
