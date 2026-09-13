using orders.Data;
using orders.Models;
using orders.Services;
using RabbitMQ.Client;

var factory = new ConnectionFactory
{
    HostName = "localhost",
    UserName = "admin",
    Password = "admin"
};

var orderService = await OrderService.CreateAsync(factory);
await orderService.StartConsumingAsync();

Console.Write("Digite seu identificador de cliente: ");
var customerId = Console.ReadLine()?.Trim();
while (string.IsNullOrWhiteSpace(customerId))
{
    Console.Write("Identificador inválido. Digite seu identificador de cliente: ");
    customerId = Console.ReadLine()?.Trim();
}

while (true)
{
    Console.WriteLine();
    Console.WriteLine("=== Menu ===");
    Console.WriteLine("1 - Ver produtos disponíveis");
    Console.WriteLine("2 - Fazer pedido");
    Console.WriteLine("3 - Excluir pedido");
    Console.WriteLine("4 - Consultar meus pedidos");
    Console.WriteLine("5 - Sair");
    Console.Write("> ");

    switch (Console.ReadLine()?.Trim())
    {
        case "1":
            ShowProducts();
            break;
        case "2":
            await CreateOrderAsync();
            break;
        case "3":
            await DeleteOrderAsync();
            break;
        case "4":
            ShowOrders(OrderRepository.ListByCustomer(customerId));
            break;
        case "5":
            return;
        default:
            Console.WriteLine("Opção inválida.");
            break;
    }
}

void ShowProducts()
{
    Console.WriteLine();
    foreach (var product in ProductCatalog.List())
    {
        Console.WriteLine($"{product.Id} - [{product.Category}] {product.Name} - R$ {product.Price} (estoque: {product.Stock})");
    }
}

async Task CreateOrderAsync()
{
    var products = ProductCatalog.List();
    var items = new List<OrderItem>();

    ShowProducts();

    while (true)
    {
        Console.Write("Id do produto (branco para finalizar o pedido): ");
        var input = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            break;
        }

        if (!int.TryParse(input, out var productId))
        {
            Console.WriteLine("Id inválido.");
            continue;
        }

        var product = products.Find(p => p.Id == productId);
        if (product is null)
        {
            Console.WriteLine("Produto não encontrado no catálogo.");
            continue;
        }

        Console.Write("Quantidade: ");
        if (!int.TryParse(Console.ReadLine(), out var quantity) || quantity <= 0)
        {
            Console.WriteLine("Quantidade inválida.");
            continue;
        }

        items.Add(new OrderItem { ProductId = product.Id, Name = product.Name, Quantity = quantity });
        Console.WriteLine($"Adicionado: {quantity}x {product.Name}");
    }

    if (items.Count == 0)
    {
        Console.WriteLine("Pedido cancelado: nenhum item adicionado.");
        return;
    }

    var order = await orderService.CreateOrderAsync(customerId, items);
    Console.WriteLine($"Pedido #{order.OrderNumber} criado e enviado para processamento.");
}

async Task DeleteOrderAsync()
{
    var myOrders = OrderRepository.ListByCustomer(customerId);
    if (myOrders.Count == 0)
    {
        Console.WriteLine("Você não possui pedidos.");
        return;
    }

    ShowOrders(myOrders);
    Console.Write("Digite o número do pedido a excluir: ");

    if (!int.TryParse(Console.ReadLine(), out var orderNumber))
    {
        Console.WriteLine("Número inválido.");
        return;
    }

    var order = myOrders.Find(o => o.OrderNumber == orderNumber);
    if (order is null)
    {
        Console.WriteLine("Pedido não encontrado.");
        return;
    }

    await orderService.DeleteOrderAsync(order);
    Console.WriteLine($"Pedido #{order.OrderNumber} excluído.");
}

void ShowOrders(List<Order> orderList)
{
    if (orderList.Count == 0)
    {
        Console.WriteLine("Você não possui pedidos.");
        return;
    }

    Console.WriteLine();
    foreach (var order in orderList)
    {
        Console.WriteLine($"#{order.OrderNumber} - {order.Status} - {order.CreatedAt:g}");
        foreach (var item in order.Items)
        {
            Console.WriteLine($"    {item.Quantity}x {item.Name} (id {item.ProductId})");
        }
    }
}
