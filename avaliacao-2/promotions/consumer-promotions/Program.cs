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
var consumer = await PromotionConsumer.CreateAsync(factory, interests);
await consumer.StartConsumingAsync();

Console.WriteLine("Consumidor de Promoções iniciado. Pressione [enter] para encerrar.");
Console.ReadLine();
