using System.Text.Encodings.Web;
using System.Text.Json;
using System.Linq;
using orders.Models;

namespace orders.Data;

/// <summary>
/// Lê e grava os pedidos diretamente em avaliacao-2/orders.json a cada operação,
/// no mesmo molde/estilo do product-catalog.json.
/// </summary>
public static class OrderRepository
{
    private const string OrdersPath = "../../orders.json";
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static List<Order> ListByCustomer(string customerId)
    {
        return ReadOrders().FindAll(o => o.CustomerId == customerId);
    }

    public static void Add(Order order)
    {
        var orders = ReadOrders();
        order.OrderNumber = orders.Count == 0 ? 1 : orders.Max(o => o.OrderNumber) + 1;
        orders.Add(order);
        WriteOrders(orders);
    }

    public static void Remove(Guid orderId)
    {
        var orders = ReadOrders();
        orders.RemoveAll(o => o.OrderId == orderId);
        WriteOrders(orders);
    }

    public static void UpdateStatus(Guid orderId, string status)
    {
        var orders = ReadOrders();
        var order = orders.Find(o => o.OrderId == orderId);
        if (order is not null)
        {
            order.Status = status;
            WriteOrders(orders);
        }
    }

    private static List<Order> ReadOrders()
    {
        if (!File.Exists(OrdersPath))
        {
            return [];
        }

        var json = File.ReadAllText(OrdersPath);
        return JsonSerializer.Deserialize<List<Order>>(json, ReadOptions) ?? [];
    }

    private static void WriteOrders(List<Order> orders)
    {
        File.WriteAllText(OrdersPath, JsonSerializer.Serialize(orders, WriteOptions));
    }
}
