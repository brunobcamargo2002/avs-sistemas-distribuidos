namespace stock.Models;

/// <summary>
/// Corpo esperado nos eventos "pedido.criado" e "pedido.excluido", no mesmo
/// molde do product-catalog.json (categoria + nome identificam o produto).
/// </summary>
public class Order
{
    public Guid OrderId { get; set; }
    public List<OrderItem> Items { get; set; } = [];
}
