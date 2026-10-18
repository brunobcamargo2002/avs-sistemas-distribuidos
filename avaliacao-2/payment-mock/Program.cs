using payment_mock.Endpoints;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("PAYMENT_MOCK_URLS") ?? "http://localhost:5083");
builder.Services.AddHttpClient();

var app = builder.Build();

app.MapChargeEndpoints();
app.MapCheckoutEndpoints();

await app.RunAsync();
