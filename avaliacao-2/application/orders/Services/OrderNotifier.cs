using System.Collections.Concurrent;
using System.Net.ServerSentEvents;
using System.Text.Json.Serialization;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace orders.Services;

/// <summary>Item cuja reserva voltou ao catálogo quando o pedido foi excluído.</summary>
public sealed record ReleasedItem(int ProductId, int Quantity);

/// <summary>
/// Corpo do evento SSE: o id do pedido e, só quando fazem falta, o link do
/// checkout e os itens devolvidos ao estoque. O que mudou é dito pelo label.
/// </summary>
public sealed record OrderEvent(
    Guid OrderId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PaymentUrl = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<ReleasedItem>? ReleasedItems = null);

/// <summary>
/// Ponte entre o consumidor do RabbitMQ e as conexões SSE abertas: cada cliente
/// conectado tem uma fila própria, e os handlers de evento escrevem nela.
/// </summary>
public sealed class OrderNotifier
{
    private sealed record Subscriber(string CustomerId, Channel<SseItem<OrderEvent>> Channel);

    private readonly ConcurrentDictionary<Guid, Subscriber> subscribers = new();

    /// <summary>
    /// Fluxo consumido pelo endpoint SSE. Encerra quando o cliente desconecta,
    /// momento em que o assinante é descartado.
    /// </summary>
    public async IAsyncEnumerable<SseItem<OrderEvent>> SubscribeAsync(string customerId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<SseItem<OrderEvent>>();
        subscribers[id] = new Subscriber(customerId, channel);
        Console.WriteLine($" [>] SSE conectado: cliente {customerId} ({subscribers.Count} conexões).");

        try
        {
            while (true)
            {
                // Suspende até algum handler escrever na fila deste assinante.
                var item = await channel.Reader.ReadAsync(cancellationToken);
                yield return item;
            }
        }
        finally
        {
            subscribers.TryRemove(id, out _);
            Console.WriteLine($" [<] SSE desconectado: cliente {customerId} ({subscribers.Count} conexões).");
        }
    }

    /// <summary>
    /// Envia o label e o id para as conexões do cliente dono do pedido. Não
    /// bloqueia: se ninguém estiver ouvindo, o evento não vai a lugar algum.
    /// </summary>
    public void Publish(string customerId, string label, OrderEvent payload)
    {
        var item = new SseItem<OrderEvent>(payload, label);

        foreach (var (_, subscriber) in subscribers)
        {
            if (subscriber.CustomerId == customerId)
            {
                subscriber.Channel.Writer.TryWrite(item);
            }
        }
    }
}
