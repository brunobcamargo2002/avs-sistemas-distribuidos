using System.Security.Cryptography;
using System.Text.Json;
using orders.Data;
using orders.Models;
using orders.Security;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace orders.Services;

public class OrderService
{
    private const string ExchangeName = "ecommerce";
    private const string QueueName = "fila.principal";
    private const string OrderCreatedRoutingKey = "pedido.criado";
    private const string OrderDeletedRoutingKey = "pedido.excluido";
    private const string StockOkRoutingKey = "pedido.estoque_ok";
    private const string StockUnavailableRoutingKey = "pedido.indisponivel";

    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IChannel _channel;
    private readonly RSA _signingKey;
    private readonly RSA _stockPublicKey;

    private OrderService(IChannel channel, RSA signingKey, RSA stockPublicKey)
    {
        _channel = channel;
        _signingKey = signingKey;
        _stockPublicKey = stockPublicKey;
    }

    public static async Task<OrderService> CreateAsync(ConnectionFactory factory)
    {
        var connection = await factory.CreateConnectionAsync();
        var channel = await connection.CreateChannelAsync();

        await channel.ExchangeDeclareAsync(exchange: ExchangeName, type: ExchangeType.Direct);

        await channel.QueueDeclareAsync(
            queue: QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?> { { "x-queue-type", "quorum" } });

        await channel.QueueBindAsync(queue: QueueName, exchange: ExchangeName, routingKey: StockOkRoutingKey);
        await channel.QueueBindAsync(queue: QueueName, exchange: ExchangeName, routingKey: StockUnavailableRoutingKey);

        var signingKey = KeyManager.LoadPrivateKey();
        var stockPublicKey = KeyManager.LoadStockPublicKey();

        return new OrderService(channel, signingKey, stockPublicKey);
    }

    public async Task StartConsumingAsync()
    {
        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += OnMessageReceivedAsync;

        await _channel.BasicConsumeAsync(QueueName, autoAck: false, consumer: consumer);
    }

    public async Task<Order> CreateOrderAsync(string customerId, List<OrderItem> items)
    {
        var order = new Order
        {
            OrderId = Guid.NewGuid(),
            CustomerId = customerId,
            Items = items,
            Status = "Criado",
            CreatedAt = DateTimeOffset.UtcNow
        };

        OrderRepository.Add(order);
        await PublishSignedAsync(OrderCreatedRoutingKey, order);

        return order;
    }

    public async Task DeleteOrderAsync(Order order)
    {
        OrderRepository.Remove(order.OrderId);
        await PublishSignedAsync(OrderDeletedRoutingKey, order);
    }

    private async Task OnMessageReceivedAsync(object sender, BasicDeliverEventArgs ea)
    {
        var body = ea.Body.ToArray();

        if (!TryVerifySignature(ea, body))
        {
            Console.WriteLine($" [!] Mensagem '{ea.RoutingKey}' rejeitada: assinatura inválida ou ausente.");
            await _channel.BasicNackAsync(deliveryTag: ea.DeliveryTag, multiple: false, requeue: false);
            return;
        }

        switch (ea.RoutingKey)
        {
            case StockOkRoutingKey:
                HandleStockOk(body);
                break;
            case StockUnavailableRoutingKey:
                HandleStockUnavailable(body);
                break;
        }

        await _channel.BasicAckAsync(deliveryTag: ea.DeliveryTag, multiple: false);
    }

    private void HandleStockOk(byte[] body)
    {
        var stockOk = JsonSerializer.Deserialize<StockOkEvent>(body, ReadOptions);
        if (stockOk is null)
        {
            return;
        }

        // TODO: no futuro será realizado o devido processamento (ex.: prosseguir com pagamento/entrega).
        OrderRepository.UpdateStatus(stockOk.OrderId, "Confirmado");
        Console.WriteLine($" [x] Pedido {stockOk.OrderId} -> estoque confirmado.");
    }

    private void HandleStockUnavailable(byte[] body)
    {
        var stockUnavailable = JsonSerializer.Deserialize<StockUnavailableEvent>(body, ReadOptions);
        if (stockUnavailable is null)
        {
            return;
        }

        OrderRepository.Remove(stockUnavailable.OrderId);
        Console.WriteLine($" [x] Pedido {stockUnavailable.OrderId} -> indisponível, pedido excluído.");
    }

    private async Task PublishSignedAsync<T>(string routingKey, T payload)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(payload);
        var signature = _signingKey.SignData(body, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var properties = new BasicProperties
        {
            Headers = new Dictionary<string, object?>
            {
                ["signature"] = signature
            }
        };

        await _channel.BasicPublishAsync(
            exchange: ExchangeName,
            routingKey: routingKey,
            mandatory: false,
            basicProperties: properties,
            body: body);
    }

    private bool TryVerifySignature(BasicDeliverEventArgs ea, byte[] body)
    {
        if (ea.BasicProperties.Headers is null ||
            !ea.BasicProperties.Headers.TryGetValue("signature", out var signatureValue) ||
            signatureValue is not byte[] signatureBytes)
        {
            return false;
        }

        return _stockPublicKey.VerifyData(body, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }
}
