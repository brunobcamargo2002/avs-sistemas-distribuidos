using System.Net.Http.Json;
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
    private const string PendingRoutingKey = "pagamento.pendente";
    private const string ApprovedRoutingKey = "pagamento.aprovado";
    private const string RejectedRoutingKey = "pagamento.recusado";
    private const string ApprovedStatus = "APROVADO";
    private const string RejectedStatus = "RECUSADO";
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly IConnection connection;
    private readonly IChannel consumerChannel;
    private readonly RSA signingKey;
    private readonly RSA stockPublicKey;
    private readonly HttpClient provider;
    private readonly string webhookUrl;

    private PaymentService(IConnection connection, IChannel consumerChannel, RSA signingKey, RSA stockPublicKey,
        HttpClient provider, string webhookUrl)
    {
        this.connection = connection;
        this.consumerChannel = consumerChannel;
        this.signingKey = signingKey;
        this.stockPublicKey = stockPublicKey;
        this.provider = provider;
        this.webhookUrl = webhookUrl;
    }

    public static async Task<PaymentService> CreateAsync(ConnectionFactory factory, HttpClient provider, string webhookUrl)
    {
        var connection = await factory.CreateConnectionAsync();
        var channel = await connection.CreateChannelAsync();
        await channel.ExchangeDeclareAsync(ExchangeName, ExchangeType.Direct);
        await channel.QueueDeclareAsync(QueueName, true, false, false,
            new Dictionary<string, object?> { ["x-queue-type"] = "quorum" });
        await channel.QueueBindAsync(QueueName, ExchangeName, StockOkRoutingKey);
        return new PaymentService(connection, channel, KeyManager.LoadPrivateKey(), KeyManager.LoadStockPublicKey(),
            provider, webhookUrl);
    }

    public async Task StartConsumingAsync()
    {
        var consumer = new AsyncEventingBasicConsumer(consumerChannel);
        consumer.ReceivedAsync += OnMessageReceivedAsync;
        await consumerChannel.BasicConsumeAsync(QueueName, false, consumer);
    }

    private async Task OnMessageReceivedAsync(object sender, BasicDeliverEventArgs args)
    {
        var body = args.Body.ToArray();
        if (!Verify(args, body))
        {
            Console.WriteLine(" [!] Pagamento rejeitado: assinatura inválida ou ausente.");
            await consumerChannel.BasicNackAsync(args.DeliveryTag, false, false);
            return;
        }

        var stock = JsonSerializer.Deserialize<StockOkEvent>(body, ReadOptions);
        if (stock is null)
        {
            await consumerChannel.BasicAckAsync(args.DeliveryTag, false);
            return;
        }

        try
        {
            await RequestCheckoutAsync(stock);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            // Sem link de checkout não há o que publicar: devolve a mensagem à
            // fila para tentar de novo quando o provedor voltar.
            Console.WriteLine($" [!] Mock de pagamento indisponível para o pedido {stock.OrderId}: {exception.Message}");
            await Task.Delay(TimeSpan.FromSeconds(3));
            await consumerChannel.BasicNackAsync(args.DeliveryTag, false, true);
            return;
        }

        await consumerChannel.BasicAckAsync(args.DeliveryTag, false);
    }

    /// <summary>
    /// Passo síncrono do fluxo: pede a cobrança ao provedor informando a URL de
    /// webhook e repassa o link recebido em "pagamento.pendente". Quem guarda o
    /// link e o status do pedido é o orders.
    /// </summary>
    private async Task RequestCheckoutAsync(StockOkEvent stock)
    {
        using var response = await provider.PostAsJsonAsync("/api/charges",
            new CreateChargeRequest(stock.OrderId, stock.Total, webhookUrl));
        response.EnsureSuccessStatusCode();

        var charge = await response.Content.ReadFromJsonAsync<ChargeCreatedResponse>()
            ?? throw new HttpRequestException("O mock de pagamento devolveu um corpo vazio.");

        // Roda na thread do consumidor, que é a dona do consumerChannel.
        await PublishAsync(consumerChannel, PendingRoutingKey, new PaymentPendingEvent
        {
            OrderId = stock.OrderId,
            PaymentId = charge.ChargeId,
            Amount = stock.Total,
            CheckoutUrl = charge.CheckoutUrl,
            CreatedAt = DateTimeOffset.UtcNow
        });

        Console.WriteLine($" [x] Pedido {stock.OrderId} -> cobrança {charge.ChargeId} criada: {charge.CheckoutUrl}");
    }

    /// <summary>
    /// Chamado pelo webhook do provedor: publica "pagamento.aprovado" ou
    /// "pagamento.recusado" com os dados recebidos na notificação. Devolve false
    /// quando a notificação não traz um status válido.
    /// </summary>
    public async Task<bool> ConfirmAsync(WebhookNotification notification)
    {
        var status = notification.Status?.Trim().ToUpperInvariant();
        if (status is not (ApprovedStatus or RejectedStatus) || notification.OrderId == Guid.Empty)
        {
            return false;
        }

        // Um canal por requisição: o IChannel não pode ser compartilhado entre
        // threads que publicam, e cada webhook chega numa thread do servidor.
        await using var channel = await connection.CreateChannelAsync();

        if (status == ApprovedStatus)
        {
            await PublishAsync(channel, ApprovedRoutingKey, new PaymentApprovedEvent
            {
                OrderId = notification.OrderId,
                PaymentId = notification.ChargeId,
                Amount = notification.Amount,
                ApprovedAt = notification.DecidedAt
            });
            Console.WriteLine($" [x] Pedido {notification.OrderId} -> pagamento aprovado no checkout.");
        }
        else
        {
            await PublishAsync(channel, RejectedRoutingKey, new PaymentRejectedEvent
            {
                OrderId = notification.OrderId,
                Reason = "Pagamento recusado pelo cliente no checkout.",
                AttemptedAt = notification.DecidedAt
            });
            Console.WriteLine($" [x] Pedido {notification.OrderId} -> pagamento recusado no checkout.");
        }

        return true;
    }

    /// <summary>
    /// Publica no canal informado pelo chamador, que é quem detém a posse dele.
    /// </summary>
    private async Task PublishAsync<T>(IChannel channel, string routingKey, T payload)
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

        return stockPublicKey.VerifyData(body, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }
}
