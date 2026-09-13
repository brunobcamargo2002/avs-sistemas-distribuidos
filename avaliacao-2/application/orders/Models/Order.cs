namespace orders.Models;

/// <summary>
/// Registro de pedido persistido em avaliacao-2/orders.json (mesmo molde do
/// product-catalog.json) e também o corpo publicado em "pedido.criado"/"pedido.excluido".
/// </summary>
public class Order
{
    public int OrderNumber { get; set; }
    public Guid OrderId { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public List<OrderItem> Items { get; set; } = [];
    public string Status { get; set; } = "Criado";
    public DateTimeOffset CreatedAt { get; set; }
}
