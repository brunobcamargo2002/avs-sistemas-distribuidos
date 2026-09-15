using delivery.Services;
using RabbitMQ.Client;

var factory = new ConnectionFactory
{
    HostName = "localhost",
    UserName = "admin",
    Password = "admin"
};

var service = await DeliveryService.CreateAsync(factory);
await service.StartConsumingAsync();

Console.WriteLine("Serviço de Entrega iniciado. Pressione [enter] para encerrar.");
Console.ReadLine();
