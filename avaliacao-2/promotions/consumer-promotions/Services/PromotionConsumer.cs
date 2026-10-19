using System.Security.Cryptography;
using System.Text.Json;
using consumer_promotions.Data;
using consumer_promotions.Models;
using consumer_promotions.Security;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace consumer_promotions.Services;

public class PromotionConsumer
{
    private const string ExchangeName = "promotions";
    private const string QueueName = "fila.promocoes";
    private const string FailedQueueName = "fila.promocoes.falhas";
    private const string InterestExchangeName = "ecommerce";
    private const string InterestQueueName = "fila.interesses.promocao";
    private const string InterestRoutingKey = "interesse.promocao";
    private const string InterestCancelledRoutingKey = "interesse.promocao.cancelado";
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IChannel _channel;
    private readonly IChannel _interestChannel;
    private readonly RSA _publicKey;
    private readonly RSA _ordersPublicKey;
    private readonly PromotionInterestRepository _interests;
    private readonly PromotionEmailSender _emailSender;

    private PromotionConsumer(IChannel channel, IChannel interestChannel, RSA publicKey, RSA ordersPublicKey,
        PromotionInterestRepository interests, PromotionEmailSender emailSender)
    {
        _channel = channel;
        _interestChannel = interestChannel;
        _publicKey = publicKey;
        _ordersPublicKey = ordersPublicKey;
        _interests = interests;
        _emailSender = emailSender;
    }

    public static async Task<PromotionConsumer> CreateAsync(ConnectionFactory factory, PromotionInterestRepository interests,
        PromotionEmailSender emailSender)
    {
        var publicKey = KeyManager.LoadPublicKey();
        var ordersPublicKey = KeyManager.LoadOrdersPublicKey();
        var connection = await factory.CreateConnectionAsync();
        var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true));
        await channel.ExchangeDeclareAsync(exchange: ExchangeName, type: ExchangeType.Topic);
        await channel.QueueDeclareAsync(QueueName, true, false, false,
            new Dictionary<string, object?> { ["x-queue-type"] = "quorum" });
        await channel.QueueBindAsync(QueueName, ExchangeName, "promocao.categoria.#");
        await channel.QueueDeclareAsync(FailedQueueName, true, false, false,
            new Dictionary<string, object?> { ["x-queue-type"] = "quorum" });
        await channel.BasicQosAsync(0, 1, false);

        var interestChannel = await connection.CreateChannelAsync();
        await interestChannel.ExchangeDeclareAsync(InterestExchangeName, ExchangeType.Direct);
        await interestChannel.QueueDeclareAsync(InterestQueueName, true, false, false,
            new Dictionary<string, object?> { ["x-queue-type"] = "quorum" });
        await interestChannel.QueueBindAsync(InterestQueueName, InterestExchangeName, InterestRoutingKey);
        await interestChannel.QueueBindAsync(InterestQueueName, InterestExchangeName, InterestCancelledRoutingKey);
        await interestChannel.BasicQosAsync(0, 1, false);

        return new PromotionConsumer(channel, interestChannel, publicKey, ordersPublicKey, interests, emailSender);
    }

    public async Task StartConsumingAsync()
    {
        var interestConsumer = new AsyncEventingBasicConsumer(_interestChannel);
        interestConsumer.ReceivedAsync += OnInterestReceivedAsync;
        await _interestChannel.BasicConsumeAsync(InterestQueueName, autoAck: false, consumer: interestConsumer);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += OnMessageReceivedAsync;

        await _channel.BasicConsumeAsync(QueueName, autoAck: false, consumer: consumer);
    }

    private async Task OnInterestReceivedAsync(object sender, BasicDeliverEventArgs ea)
    {
        var body = ea.Body.ToArray();
        if (!TryVerifySignature(ea, body, _ordersPublicKey))
        {
            Console.WriteLine(" [!] Interest rejected: missing or invalid Gateway signature.");
            await _interestChannel.BasicNackAsync(ea.DeliveryTag, false, false);
            return;
        }

        try
        {
            var interest = JsonSerializer.Deserialize<PromotionInterestEvent>(body, ReadOptions);
            var cancel = ea.RoutingKey == InterestCancelledRoutingKey;
            if (interest is null || interest.EventId == Guid.Empty || string.IsNullOrWhiteSpace(interest.Email) ||
                interest.OccurredAt == default || interest.Categories is null ||
                (!cancel && (interest.Categories.Count == 0 || interest.Categories.Any(string.IsNullOrWhiteSpace))))
            {
                await _interestChannel.BasicNackAsync(ea.DeliveryTag, false, false);
                return;
            }

            var updated = _interests.Apply(interest, cancel);
            Console.WriteLine(updated
                ? $" [x] Promotion interests {(cancel ? "cancelled" : "saved")} for {interest.Email}."
                : " [x] Duplicate or outdated interest event ignored.");
            await _interestChannel.BasicAckAsync(ea.DeliveryTag, false);
        }
        catch (JsonException)
        {
            Console.WriteLine(" [!] Invalid interests JSON.");
            await _interestChannel.BasicNackAsync(ea.DeliveryTag, false, false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($" [!] Interests could not be persisted: {exception.Message}");
            await _interestChannel.BasicNackAsync(ea.DeliveryTag, false, true);
        }
    }

    private async Task OnMessageReceivedAsync(object sender, BasicDeliverEventArgs ea)
    {
        var body = ea.Body.ToArray();

        if (!TryVerifySignature(ea, body, _publicKey))
        {
            Console.WriteLine($" [!] Mensagem '{ea.RoutingKey}' rejeitada: assinatura inválida ou ausente.");
            await _channel.BasicNackAsync(deliveryTag: ea.DeliveryTag, multiple: false, requeue: false);
            return;
        }

        try
        {
            var promotion = JsonSerializer.Deserialize<Promotion>(body, ReadOptions);
            if (promotion is null || promotion.Id == Guid.Empty || string.IsNullOrWhiteSpace(promotion.Category))
            {
                await _channel.BasicNackAsync(ea.DeliveryTag, false, false);
                return;
            }

            var recipients = _interests.FindRecipients(promotion.Category);
            Console.WriteLine($" [x] '{ea.RoutingKey}' -> {promotion.ProductName}: {recipients.Count} interested recipients.");
            foreach (var email in recipients)
            {
                if (!_interests.FindRecipients(promotion.Category).Contains(email, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var sent = await _emailSender.SendAsync(promotion, email);
                Console.WriteLine(sent
                    ? $" [x] Promotion {promotion.Id} accepted by the email provider."
                    : $" [x] Promotion {promotion.Id}: previously sent email skipped.");
            }

            await _channel.BasicAckAsync(deliveryTag: ea.DeliveryTag, multiple: false);
        }
        catch (JsonException)
        {
            Console.WriteLine(" [!] Invalid promotion or interests JSON.");
            await _channel.BasicNackAsync(ea.DeliveryTag, false, false);
        }
        catch (HttpRequestException exception) when (exception.StatusCode is { } status &&
            (int)status < 500 && (int)status is not (408 or 409 or 429))
        {
            try
            {
                await _channel.BasicPublishAsync(
                    exchange: "", routingKey: FailedQueueName, mandatory: true,
                    basicProperties: new BasicProperties
                    {
                        Persistent = true,
                        Headers = new Dictionary<string, object?>
                        {
                            ["signature"] = ea.BasicProperties.Headers!["signature"],
                            ["original-routing-key"] = ea.RoutingKey,
                            ["email-provider-status"] = (int)exception.StatusCode.Value
                        }
                    }, body: body);
                await _channel.BasicAckAsync(ea.DeliveryTag, false);
                Console.WriteLine($" [!] Email provider rejected promotion (HTTP {(int)exception.StatusCode.Value}); retained in {FailedQueueName}.");
            }
            catch (Exception publishException) when (publishException is RabbitMQ.Client.Exceptions.RabbitMQClientException or IOException)
            {
                Console.WriteLine(" [!] Failed to retain promotion in the failure queue; requeuing original message.");
                await _channel.BasicNackAsync(ea.DeliveryTag, false, true);
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($" [!] Email processing failed ({exception.GetType().Name}); retrying promotion.");
            await Task.Delay(TimeSpan.FromSeconds(5));
            await _channel.BasicNackAsync(ea.DeliveryTag, false, true);
        }
    }

    private static bool TryVerifySignature(BasicDeliverEventArgs ea, byte[] body, RSA publicKey)
    {
        if (ea.BasicProperties.Headers is null ||
            !ea.BasicProperties.Headers.TryGetValue("signature", out var signatureValue) ||
            signatureValue is not byte[] signatureBytes)
        {
            return false;
        }

        return publicKey.VerifyData(body, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }
}
