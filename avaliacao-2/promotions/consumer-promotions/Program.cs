using consumer_promotions.Services;
using RabbitMQ.Client;

var factory = new ConnectionFactory
{
    HostName = "localhost",
    UserName = "admin",
    Password = "admin"
};

var categoryPatterns = CategoryPrompt.ReadCategoriesOfInterest();

var consumer = await PromotionConsumer.CreateAsync(factory, categoryPatterns);
await consumer.StartConsumingAsync();

Console.WriteLine("Consumidor de Promoções iniciado. Pressione [enter] para encerrar.");
Console.ReadLine();
