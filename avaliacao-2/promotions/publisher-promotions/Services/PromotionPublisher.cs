using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using publisher_promotions.Models;
using publisher_promotions.Security;
using RabbitMQ.Client;

namespace publisher_promotions.Services;

public class PromotionPublisher
{
    private const string ExchangeName = "promotions";

    private readonly IConnection _connection;
    private readonly IChannel _channel;
    private readonly RSA _signingKey;

    private PromotionPublisher(IConnection connection, IChannel channel, RSA signingKey)
    {
        _connection = connection;
        _channel = channel;
        _signingKey = signingKey;
    }

    public static async Task<PromotionPublisher> CreateAsync(ConnectionFactory factory)
    {
        var connection = await factory.CreateConnectionAsync();
        var channel = await connection.CreateChannelAsync();
        await channel.ExchangeDeclareAsync(exchange: ExchangeName, type: ExchangeType.Topic);

        var signingKey = KeyManager.LoadPrivateKey();

        return new PromotionPublisher(connection, channel, signingKey);
    }

    public async Task PublishAsync(Promotion promotion)
    {
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(promotion));
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
            routingKey: promotion.RoutingKey,
            mandatory: false,
            basicProperties: properties,
            body: body);
    }
}
