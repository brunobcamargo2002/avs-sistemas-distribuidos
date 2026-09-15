using System.Security.Cryptography;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using stock.Data;
using stock.Models;
using stock.Security;

namespace stock.Services;

public class StockService
{
    private const string ExchangeName = "ecommerce";
    private const string QueueName = "fila.estoque";
    private const string OrderCreatedRoutingKey = "pedido.criado";
    private const string OrderDeletedRoutingKey = "pedido.excluido";
    private const string StockOkRoutingKey = "pedido.estoque_ok";
    private const string StockUnavailableRoutingKey = "pedido.indisponivel";

    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IChannel _channel;
    private readonly RSA _signingKey;
    private readonly RSA _ordersPublicKey;

    private StockService(IChannel channel, RSA signingKey, RSA ordersPublicKey)
    {
        _channel = channel;
        _signingKey = signingKey;
        _ordersPublicKey = ordersPublicKey;
    }

    public static async Task<StockService> CreateAsync(ConnectionFactory factory)
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

        await channel.QueueBindAsync(queue: QueueName, exchange: ExchangeName, routingKey: OrderCreatedRoutingKey);
        await channel.QueueBindAsync(queue: QueueName, exchange: ExchangeName, routingKey: OrderDeletedRoutingKey);

        var signingKey = KeyManager.LoadPrivateKey();
        var ordersPublicKey = KeyManager.LoadOrdersPublicKey();

        return new StockService(channel, signingKey, ordersPublicKey);
    }

    public async Task StartConsumingAsync()
    {
        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += OnMessageReceivedAsync;

        await _channel.BasicConsumeAsync(QueueName, autoAck: false, consumer: consumer);
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

        var order = JsonSerializer.Deserialize<Order>(body, ReadOptions);
        if (order is null)
        {
            Console.WriteLine($" [!] Mensagem '{ea.RoutingKey}' rejeitada: corpo inválido.");
            await _channel.BasicNackAsync(deliveryTag: ea.DeliveryTag, multiple: false, requeue: false);
            return;
        }

        switch (ea.RoutingKey)
        {
            case OrderCreatedRoutingKey:
                await HandleOrderCreatedAsync(order);
                break;
            case OrderDeletedRoutingKey:
                HandleOrderDeleted(order);
                break;
        }

        await _channel.BasicAckAsync(deliveryTag: ea.DeliveryTag, multiple: false);
    }

    private async Task HandleOrderCreatedAsync(Order order)
    {
        var unavailableItems = ProductCatalog.GetUnavailableItems(order.Items);

        if (unavailableItems.Count == 0)
        {
            ProductCatalog.Reserve(order.Items);
            await PublishSignedAsync(StockOkRoutingKey, new StockOkEvent
            {
                OrderId = order.OrderId,
                Items = order.Items,
                Total = ProductCatalog.CalculateTotal(order.Items),
                ProcessedAt = DateTimeOffset.UtcNow
            });

            Console.WriteLine($" [x] Pedido {order.OrderId} -> estoque OK, baixa realizada.");
        }
        else
        {
            await PublishSignedAsync(StockUnavailableRoutingKey, new StockUnavailableEvent
            {
                OrderId = order.OrderId,
                UnavailableItems = unavailableItems,
                ProcessedAt = DateTimeOffset.UtcNow
            });

            Console.WriteLine($" [x] Pedido {order.OrderId} -> indisponível ({unavailableItems.Count} item(ns)).");
        }
    }

    private void HandleOrderDeleted(Order order)
    {
        ProductCatalog.Release(order.Items);
        Console.WriteLine($" [x] Pedido {order.OrderId} -> excluído, estoque restaurado.");
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

        return _ordersPublicKey.VerifyData(body, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }
}
