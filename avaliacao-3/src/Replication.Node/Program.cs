using System.Text.Json;
using Replication.Node.Hosting;
using Replication.Node.Storage;

var path = Path.Combine(AppContext.BaseDirectory, "data", "state.json");
var port = 7001;
var seen = new HashSet<string>(StringComparer.Ordinal);

for (var index = 0; index < args.Length; index += 2)
{
    if (index + 1 >= args.Length || !seen.Add(args[index]))
    {
        return Usage();
    }

    switch (args[index])
    {
        case "--data" when !string.IsNullOrWhiteSpace(args[index + 1]):
            path = args[index + 1];
            break;
        case "--port" when int.TryParse(args[index + 1], out var parsedPort)
            && parsedPort is >= 1 and <= 65535:
            port = parsedPort;
            break;
        default:
            return Usage();
    }
}

try
{
    await using var app = NodeApplication.Create(path, port);
    var store = app.Services.GetRequiredService<JsonKeyValueStore>();
    Console.WriteLine("Servidor gRPC - etapa 2 (sem replicacao)");
    Console.WriteLine($"Arquivo: {store.FilePath}");
    Console.WriteLine($"Endereco: http://127.0.0.1:{port}");
    await app.RunAsync();
    return 0;
}
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
    or JsonException or ArgumentException or NotSupportedException)
{
    Console.Error.WriteLine($"Nao foi possivel executar o servidor: {exception.Message}");
    return 1;
}

static int Usage()
{
    Console.Error.WriteLine("Uso: Replication.Node [--data <caminho-do-json>] [--port <1-65535>]");
    return 1;
}
