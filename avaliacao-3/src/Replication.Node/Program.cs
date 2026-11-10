using System.Text.Json;
using Replication.Node.Hosting;
using Replication.Node.Storage;
using Replication.Node.Replication;

var path = Path.Combine(AppContext.BaseDirectory, "data", "state.json");
var port = 7001;
var nodeOptions = new FixedNodeOptions();
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
        case "--node-id" when !string.IsNullOrWhiteSpace(args[index + 1]):
            nodeOptions = nodeOptions with { NodeId = args[index + 1] };
            break;
        case "--role" when args[index + 1] is "leader" or "follower":
            nodeOptions = nodeOptions with { IsLeader = args[index + 1] == "leader" };
            break;
        case "--leader":
            nodeOptions = nodeOptions with { LeaderAddress = args[index + 1] };
            break;
        case "--members":
            nodeOptions = nodeOptions with { Members = args[index + 1].Split(',', StringSplitOptions.TrimEntries) };
            break;
        default:
            return Usage();
    }
}

try
{
    if (!nodeOptions.IsLeader && !seen.Contains("--node-id"))
    {
        throw new ArgumentException("Informe um --node-id exclusivo para cada seguidor.");
    }

    await using var app = NodeApplication.Create(path, port, nodeOptions);
    var store = app.Services.GetRequiredService<JsonKeyValueStore>();
    if (!nodeOptions.IsLeader)
    {
        if (new Uri(nodeOptions.LeaderAddress!).Port == port)
        {
            throw new ArgumentException("A porta do seguidor deve ser diferente da porta do lider.");
        }

        store.EnsureLogBackedState();
    }

    Console.WriteLine("Servidor gRPC - etapa 5 (lider fixo, quorum=3/4, sem eleicao)");
    var status = store.GetStatus();
    Console.WriteLine($"No: {nodeOptions.NodeId}; papel: {(nodeOptions.IsLeader ? "lider fixo" : "seguidor")}; termo: 0; commit: {status.CommitIndex}");
    if (!nodeOptions.IsLeader)
    {
        Console.WriteLine($"Lider: {nodeOptions.LeaderAddress}");
    }
    Console.WriteLine($"Arquivo: {store.FilePath}");
    Console.WriteLine($"Endereco: http://127.0.0.1:{port}");
    await app.RunAsync();
    return 0;
}
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
    or JsonException or ArgumentException or NotSupportedException or InvalidOperationException)
{
    Console.Error.WriteLine($"Nao foi possivel executar o servidor: {exception.Message}");
    return 1;
}

static int Usage()
{
    Console.Error.WriteLine("Uso: Replication.Node [--data <json>] [--port <1-65535>] "
        + "[--node-id <id>] [--role leader|follower] [--leader http://127.0.0.1:porta] "
        + "[--members node-1,node-2,node-3,node-4]");
    return 1;
}
