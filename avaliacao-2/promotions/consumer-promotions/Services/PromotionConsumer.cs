using System.Security.Cryptography;
using System.Text.Json;
using consumer_promotions.Models;
using consumer_promotions.Security;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace consumer_promotions.Services;

public class PromotionConsumer
{
    private const string ExchangeName = "promotions";

    private readonly IChannel _channel;
    private readonly string _queueName;
    private readonly RSA _publicKey;

    private PromotionConsumer(IChannel channel, string queueName, RSA publicKey)
    {
        _channel = channel;
        _queueName = queueName;
        _publicKey = publicKey;
    }

    public static async Task<PromotionConsumer> CreateAsync(ConnectionFactory factory, IEnumerable<string> routingKeyPatterns)
    {
        var connection = await factory.CreateConnectionAsync();
        var channel = await connection.CreateChannelAsync();
        await channel.ExchangeDeclareAsync(exchange: ExchangeName, type: ExchangeType.Topic);

        var queueDeclareResult = await channel.QueueDeclareAsync();
        var queueName = queueDeclareResult.QueueName;

        foreach (var pattern in routingKeyPatterns)
        {
            await channel.QueueBindAsync(queue: queueName, exchange: ExchangeName, routingKey: pattern);
            Console.WriteLine($"Inscrito em '{pattern}'.");
        }

        var publicKey = KeyManager.LoadPublicKey();

        return new PromotionConsumer(channel, queueName, publicKey);
    }

    public async Task StartConsumingAsync()
    {
        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += OnMessageReceivedAsync;

        await _channel.BasicConsumeAsync(_queueName, autoAck: false, consumer: consumer);
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

        var promotion = JsonSerializer.Deserialize<Promotion>(body);

        Console.WriteLine($" [x] '{ea.RoutingKey}' -> {promotion?.ProductName} " +
                           $"({promotion?.DiscountPercentage}% off, R$ {promotion?.OriginalPrice} -> R$ {promotion?.PromotionalPrice})");

        await _channel.BasicAckAsync(deliveryTag: ea.DeliveryTag, multiple: false);
    }

    private bool TryVerifySignature(BasicDeliverEventArgs ea, byte[] body)
    {
        if (ea.BasicProperties.Headers is null ||
            !ea.BasicProperties.Headers.TryGetValue("signature", out var signatureValue) ||
            signatureValue is not byte[] signatureBytes)
        {
            return false;
        }

        return _publicKey.VerifyData(body, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }
}
