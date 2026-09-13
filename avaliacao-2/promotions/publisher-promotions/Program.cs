using publisher_promotions.Services;
using RabbitMQ.Client;

var factory = new ConnectionFactory
{
    HostName = "localhost",
    UserName = "admin",
    Password = "admin"
};

var publisher = await PromotionPublisher.CreateAsync(factory);

Console.WriteLine("Publisher de Promoções iniciado. Pressione Ctrl+C para encerrar.");

while (true)
{
    var promotion = PromotionGenerator.GenerateRandom();
    await publisher.PublishAsync(promotion);

    Console.WriteLine($"[{promotion.GeneratedAt:HH:mm:ss}] Publicado '{promotion.RoutingKey}' -> {promotion.ProductName} " +
                       $"({promotion.DiscountPercentage}% off, R$ {promotion.OriginalPrice} -> R$ {promotion.PromotionalPrice})");

    var delaySeconds = Random.Shared.Next(10, 20); // entre 30s e 3min
    await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
}
