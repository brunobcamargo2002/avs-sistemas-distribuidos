using System.Text.Json;
using Replication.Node.Storage;

if (args.Length != 0 && (args.Length != 2 || args[0] != "--data"))
{
    Console.Error.WriteLine("Uso: Replication.Node [--data <caminho-do-json>]");
    return 1;
}

var path = args.Length == 2
    ? args[1]
    : Path.Combine(AppContext.BaseDirectory, "data", "state.json");

try
{
    using var store = new JsonKeyValueStore(path);
    Console.WriteLine("Armazenamento local - etapa 1 (sem rede ou replicacao)");
    Console.WriteLine($"Arquivo: {store.FilePath}");

    while (true)
    {
        Console.WriteLine();
        Console.WriteLine("1 - Gravar | 2 - Consultar | 3 - Listar | 0 - Sair");
        Console.Write("> ");
        var option = Console.ReadLine();
        if (option is null or "0")
        {
            return 0;
        }

        switch (option)
        {
            case "1":
                Console.Write("Chave: ");
                var key = Console.ReadLine();
                if (key is null)
                {
                    return 0;
                }

                if (string.IsNullOrWhiteSpace(key))
                {
                    Console.Error.WriteLine("A chave nao pode ser vazia.");
                    break;
                }

                Console.Write("Valor: ");
                var value = Console.ReadLine();
                if (value is null)
                {
                    return 0;
                }

                try
                {
                    store.Put(key, value);
                    Console.WriteLine("Dado persistido com sucesso.");
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    Console.Error.WriteLine($"Falha ao persistir o dado: {exception.Message}");
                    return 1;
                }

                break;
            case "2":
                Console.Write("Chave: ");
                var lookupKey = Console.ReadLine();
                if (lookupKey is null)
                {
                    return 0;
                }

                if (string.IsNullOrWhiteSpace(lookupKey))
                {
                    Console.Error.WriteLine("A chave nao pode ser vazia.");
                    break;
                }

                Console.WriteLine(store.TryGet(lookupKey, out var storedValue)
                    ? $"Valor: {storedValue}"
                    : "Chave nao encontrada.");
                break;
            case "3":
                var entries = store.GetAll();
                if (entries.Count == 0)
                {
                    Console.WriteLine("Nenhum dado armazenado.");
                }

                foreach (var entry in entries.OrderBy(entry => entry.Key, StringComparer.Ordinal))
                {
                    Console.WriteLine($"{entry.Key} = {entry.Value}");
                }

                break;
            default:
                Console.Error.WriteLine("Opcao invalida.");
                break;
        }
    }
}
catch (Exception exception) when (exception is IOException
    or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
{
    Console.Error.WriteLine($"Nao foi possivel abrir o armazenamento: {exception.Message}");
    return 1;
}
