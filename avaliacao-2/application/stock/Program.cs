using RabbitMQ.Client;
using stock.Services;

var factory = new ConnectionFactory
{
    HostName = "localhost",
    UserName = "admin",
    Password = "admin"
};

var stockService = await StockService.CreateAsync(factory);
await stockService.StartConsumingAsync();

Console.WriteLine("Serviço de Estoque iniciado. Pressione [enter] para encerrar.");
Console.ReadLine();
