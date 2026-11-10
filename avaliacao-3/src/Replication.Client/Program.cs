using Grpc.Core;
using Grpc.Net.Client;
using Replication.Contracts;

var address = new Uri("http://127.0.0.1:7001");
var timeoutSeconds = 5;
var seen = new HashSet<string>(StringComparer.Ordinal);

for (var index = 0; index < args.Length; index += 2)
{
    if (index + 1 >= args.Length || !seen.Add(args[index]))
    {
        return Usage();
    }

    switch (args[index])
    {
        case "--server" when Uri.TryCreate(args[index + 1], UriKind.Absolute, out var parsedAddress)
            && parsedAddress.Scheme is "http" or "https"
            && parsedAddress.AbsolutePath == "/" && parsedAddress.Query == ""
            && parsedAddress.Fragment == "" && parsedAddress.UserInfo == "":
            address = parsedAddress;
            break;
        case "--timeout-seconds" when int.TryParse(args[index + 1], out var parsedTimeout)
            && parsedTimeout is >= 1 and <= 300:
            timeoutSeconds = parsedTimeout;
            break;
        default:
            return Usage();
    }
}

using var channel = GrpcChannel.ForAddress(address);
var client = new Storage.StorageClient(channel);
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

Console.WriteLine($"Cliente gRPC - servidor: {address}");
Console.WriteLine($"Timeout por chamada: {timeoutSeconds}s. Ctrl+C cancela uma chamada em andamento.");

while (!cancellation.IsCancellationRequested)
{
    Console.WriteLine();
    Console.WriteLine("1 - Gravar | 2 - Consultar | 3 - Listar dados | 4 - Ver log | 0 - Sair");
    Console.Write("> ");
    var option = Console.ReadLine();
    if (option is null or "0")
    {
        return 0;
    }

    try
    {
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

                var write = await client.WriteAsync(new WriteRequest { Key = key, Value = value },
                    deadline: DateTime.UtcNow.AddSeconds(timeoutSeconds),
                    cancellationToken: cancellation.Token);
                Console.WriteLine(
                    $"Dado comitado no indice {write.LogIndex} (commit={write.CommitIndex}, aplicado={write.LastApplied}).");
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

                var result = await client.ReadAsync(new ReadRequest { Key = lookupKey },
                    deadline: DateTime.UtcNow.AddSeconds(timeoutSeconds),
                    cancellationToken: cancellation.Token);
                Console.WriteLine($"Valor: {result.Value}");
                break;
            case "3":
                var listing = await client.ListAsync(new ListRequest(),
                    deadline: DateTime.UtcNow.AddSeconds(timeoutSeconds),
                    cancellationToken: cancellation.Token);
                if (listing.Entries.Count == 0)
                {
                    Console.WriteLine("Nenhum dado armazenado.");
                }

                foreach (var entry in listing.Entries)
                {
                    Console.WriteLine($"{entry.Key} = {entry.Value}");
                }

                break;
            case "4":
                var log = await client.GetLogAsync(new GetLogRequest(),
                    deadline: DateTime.UtcNow.AddSeconds(timeoutSeconds),
                    cancellationToken: cancellation.Token);
                Console.WriteLine($"No: {log.NodeId}; papel: {log.Role}");
                if (!string.IsNullOrEmpty(log.LeaderAddress))
                {
                    Console.WriteLine($"Lider fixo: {log.LeaderAddress}");
                }
                Console.WriteLine(
                    $"Ultimo indice: {log.LastLogIndex}; commit: {log.CommitIndex}; aplicado: {log.LastApplied}");
                if (log.Entries.Count == 0)
                {
                    Console.WriteLine("Log vazio.");
                }

                foreach (var entry in log.Entries)
                {
                    var state = entry.IsCommitted ? "comitado" : "nao comitado";
                    Console.WriteLine(
                        $"[{entry.Index}] termo={entry.Term} {state}: {entry.Key} = {entry.Value}");
                }

                break;
            default:
                Console.Error.WriteLine("Opcao invalida.");
                break;
        }
    }
    catch (RpcException exception)
    {
        var message = exception.StatusCode switch
        {
            StatusCode.NotFound => "Chave nao encontrada.",
            StatusCode.Unavailable => "Servidor indisponivel. Confira o endereco e se ele esta em execucao.",
            StatusCode.DeadlineExceeded => "Tempo limite da chamada excedido.",
            StatusCode.Cancelled => "Chamada cancelada.",
            _ => $"Falha gRPC ({exception.StatusCode}): {exception.Status.Detail}"
        };
        Console.Error.WriteLine(message);
        var leaderAddress = exception.Trailers.GetValue("leader-address");
        if (!string.IsNullOrEmpty(leaderAddress))
        {
            Console.Error.WriteLine($"Conecte o cliente ao lider usando --server {leaderAddress}");
        }
        if (option == "1")
        {
            Console.Error.WriteLine(
                "A escrita nao foi confirmada. Em falhas de comunicacao, consulte a chave antes de repetir.");
        }
    }
}

return 0;

static int Usage()
{
    Console.Error.WriteLine(
        "Uso: Replication.Client [--server <http(s)://host:porta>] [--timeout-seconds <1-300>]");
    return 1;
}
