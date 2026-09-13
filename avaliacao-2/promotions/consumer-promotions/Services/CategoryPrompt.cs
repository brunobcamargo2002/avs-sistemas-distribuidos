namespace consumer_promotions.Services;

/// <summary>
/// Lê do console as categorias de promoções que o cliente tem interesse em receber.
/// Usa a sintaxe de topic exchange do RabbitMQ: '#' (zero ou mais palavras) e
/// '*' (exatamente uma palavra), permitindo assinar todas as categorias, um grupo
/// de subcategorias, ou uma categoria específica.
/// </summary>
public static class CategoryPrompt
{
    private const string RoutingKeyPrefix = "promocao.categoria.";

    public static List<string> ReadCategoriesOfInterest()
    {
        Console.WriteLine("Adicione as categorias de promoções de interesse. Exemplos:");
        Console.WriteLine("  #                        -> todas as categorias");
        Console.WriteLine("  hardware.#               -> hardware e todas as subcategorias");
        Console.WriteLine("  hardware.placa_de_video  -> categoria específica");
        Console.WriteLine("Digite uma categoria por linha. Deixe em branco para finalizar.");

        var routingKeys = new List<string>();

        while (true)
        {
            Console.Write("> ");
            var input = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input))
            {
                if (routingKeys.Count > 0)
                {
                    return routingKeys;
                }

                Console.WriteLine("Adicione ao menos uma categoria antes de continuar.");
                continue;
            }

            routingKeys.Add(RoutingKeyPrefix + input.Trim());
        }
    }
}
