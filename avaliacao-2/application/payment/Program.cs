using payment.Services;
using RabbitMQ.Client;

var factory = new ConnectionFactory
{
    HostName = "localhost",
    UserName = "admin",
    Password = "admin"
};

var service = await PaymentService.CreateAsync(factory);
await service.StartConsumingAsync();

Console.WriteLine("Serviço de Pagamento iniciado. Pressione [enter] para encerrar.");
Console.ReadLine();
