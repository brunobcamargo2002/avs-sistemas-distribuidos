using System.Security.Cryptography;
using System.Text.Json;
using payment.Models;
using payment.Security;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace payment.Services;

public class PaymentService
{
    private const string ExchangeName = "ecommerce";
    private const string QueueName = "fila.pagamento";
    private const string StockOkRoutingKey = "pedido.estoque_ok";
    private const string ApprovedRoutingKey = "pagamento.aprovado";
    private const string RejectedRoutingKey = "pagamento.recusado";
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly IChannel channel;
    private readonly RSA signingKey;
    private readonly RSA ordersPublicKey;
    private readonly HashSet<Guid> processedOrders = [];

    private PaymentService(IChannel channel, RSA signingKey, RSA ordersPublicKey)
    {
        this.channel = channel;
        this.signingKey = signingKey;
        this.ordersPublicKey = ordersPublicKey;
    }

    public static async Task<PaymentService> CreateAsync(ConnectionFactory factory)
    {
        var channel = await (await factory.CreateConnectionAsync()).CreateChannelAsync();
        await channel.ExchangeDeclareAsync(ExchangeName, ExchangeType.Direct);
        await channel.QueueDeclareAsync(QueueName, true, false, false,
            new Dictionary<string, object?> { ["x-queue-type"] = "quorum" });
        await channel.QueueBindAsync(QueueName, ExchangeName, StockOkRoutingKey);
        return new PaymentService(channel, KeyManager.LoadPrivateKey(), KeyManager.LoadOrdersPublicKey());
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
            Console.WriteLine(" [!] Pagamento rejeitado: assinatura inválida ou ausente.");
            await channel.BasicNackAsync(args.DeliveryTag, false, false);
            return;
        }

        var stock = JsonSerializer.Deserialize<StockOkEvent>(body, ReadOptions);
        if (stock is null || !processedOrders.Add(stock.OrderId))
        {
            await channel.BasicAckAsync(args.DeliveryTag, false);
            return;
        }

        if (Random.Shared.Next(100) < 80)
        {
            await PublishAsync(ApprovedRoutingKey, new PaymentApprovedEvent
            {
                OrderId = stock.OrderId,
                PaymentId = Guid.NewGuid(),
                Amount = stock.Total,
                ApprovedAt = DateTimeOffset.UtcNow
            });
            Console.WriteLine($" [x] Pedido {stock.OrderId} -> pagamento aprovado.");
        }
        else
        {
            await PublishAsync(RejectedRoutingKey, new PaymentRejectedEvent
            {
                OrderId = stock.OrderId,
                Reason = "Pagamento recusado pelo simulador.",
                AttemptedAt = DateTimeOffset.UtcNow
            });
            Console.WriteLine($" [x] Pedido {stock.OrderId} -> pagamento recusado.");
        }

        await channel.BasicAckAsync(args.DeliveryTag, false);
    }

    private async Task PublishAsync<T>(string routingKey, T payload)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(payload);
        var signature = signingKey.SignData(body, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        await channel.BasicPublishAsync(ExchangeName, routingKey, false,
            new BasicProperties { Headers = new Dictionary<string, object?> { ["signature"] = signature } }, body);
    }

    private bool Verify(BasicDeliverEventArgs args, byte[] body)
    {
        if (args.BasicProperties.Headers is null ||
            !args.BasicProperties.Headers.TryGetValue("signature", out var value) || value is not byte[] signature)
        {
            return false;
        }

        return ordersPublicKey.VerifyData(body, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }
}
