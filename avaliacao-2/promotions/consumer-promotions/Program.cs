using consumer_promotions.Data;
using consumer_promotions.Services;
using RabbitMQ.Client;

var factory = new ConnectionFactory
{
    HostName = "localhost",
    UserName = "admin",
    Password = "admin"
};

var interests = new PromotionInterestRepository(
    Environment.GetEnvironmentVariable("PROMOTION_INTERESTS_PATH") ?? "promotion-interests.json");
var deliveries = new PromotionDeliveryRepository(
    Environment.GetEnvironmentVariable("PROMOTION_DELIVERIES_PATH") ?? "promotion-deliveries.json");
using var emailClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
var emailSender = new PromotionEmailSender(emailClient,
    Environment.GetEnvironmentVariable("RESEND_API_KEY") ?? "",
    Environment.GetEnvironmentVariable("PROMOTIONS_FROM_EMAIL") ?? "", deliveries);
var consumer = await PromotionConsumer.CreateAsync(factory, interests, emailSender);
await consumer.StartConsumingAsync();

Console.WriteLine("Consumidor de Promoções iniciado. Pressione [enter] para encerrar.");
Console.ReadLine();
