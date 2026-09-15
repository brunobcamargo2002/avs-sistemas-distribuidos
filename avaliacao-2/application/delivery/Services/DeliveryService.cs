using System.Security.Cryptography;
using System.Text.Json;
using delivery.Models;
using delivery.Security;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace delivery.Services;

public class DeliveryService
{
    private const string ExchangeName = "ecommerce";
    private const string QueueName = "fila.entrega";
    private const string PaymentApprovedRoutingKey = "pagamento.aprovado";
    private const string OrderShippedRoutingKey = "pedido.enviado";
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly IChannel channel;
    private readonly RSA signingKey;
    private readonly RSA paymentPublicKey;
    private readonly HashSet<Guid> processedOrders = [];

    private DeliveryService(IChannel channel, RSA signingKey, RSA paymentPublicKey)
    {
        this.channel = channel;
        this.signingKey = signingKey;
        this.paymentPublicKey = paymentPublicKey;
    }

    public static async Task<DeliveryService> CreateAsync(ConnectionFactory factory)
    {
        var channel = await (await factory.CreateConnectionAsync()).CreateChannelAsync();
        await channel.ExchangeDeclareAsync(ExchangeName, ExchangeType.Direct);
        await channel.QueueDeclareAsync(QueueName, true, false, false,
            new Dictionary<string, object?> { ["x-queue-type"] = "quorum" });
        await channel.QueueBindAsync(QueueName, ExchangeName, PaymentApprovedRoutingKey);
        return new DeliveryService(channel, KeyManager.LoadPrivateKey(), KeyManager.LoadPaymentPublicKey());
    }

    public async Task StartConsumingAsync()
    {
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += OnMessageReceivedAsync;
        await channel.BasicConsumeAsync(QueueName, false, consumer);
    }

    private async Task OnMessageReceivedAsync(object sender, BasicDeliverEventArgs args)
    {
        var body = args.Body.ToArray();
        if (!Verify(args, body))
        {
            Console.WriteLine(" [!] Entrega rejeitada: assinatura inválida ou ausente.");
            await channel.BasicNackAsync(args.DeliveryTag, false, false);
            return;
        }

        var payment = JsonSerializer.Deserialize<PaymentApprovedEvent>(body, ReadOptions);
        if (payment is null || !processedOrders.Add(payment.OrderId))
        {
            await channel.BasicAckAsync(args.DeliveryTag, false);
            return;
        }

        var shipment = new OrderShippedEvent
        {
            OrderId = payment.OrderId,
            InvoiceId = Guid.NewGuid(),
            TrackingCode = $"BR{Random.Shared.NextInt64(100000000, 999999999)}",
            ShippedAt = DateTimeOffset.UtcNow
        };

        await PublishAsync(shipment);
        Console.WriteLine($" [x] Pedido {shipment.OrderId} -> nota emitida e despacho preparado ({shipment.TrackingCode}).");
        await channel.BasicAckAsync(args.DeliveryTag, false);
    }

    private async Task PublishAsync(OrderShippedEvent shipment)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(shipment);
        var signature = signingKey.SignData(body, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        await channel.BasicPublishAsync(ExchangeName, OrderShippedRoutingKey, false,
            new BasicProperties { Headers = new Dictionary<string, object?> { ["signature"] = signature } }, body);
    }

    private bool Verify(BasicDeliverEventArgs args, byte[] body)
    {
        if (args.BasicProperties.Headers is null ||
            !args.BasicProperties.Headers.TryGetValue("signature", out var value) || value is not byte[] signature)
        {
            return false;
        }

        return paymentPublicKey.VerifyData(body, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }
}
