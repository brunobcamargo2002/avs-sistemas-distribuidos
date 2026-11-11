using Grpc.Core;
using Grpc.Net.Client;
using Replication.Contracts;

namespace Replication.Client;

public static class ClusterWriter
{
    public static async Task<WriteResponse> WriteAsync(
        IEnumerable<Uri> addresses, WriteRequest request, TimeSpan timeout, CancellationToken token)
    {
        var candidates = addresses.Select(address => address.AbsoluteUri).Distinct(StringComparer.Ordinal).ToList();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var statuses = new List<(string Address, GetLogResponse Status)>();
        while (candidates.Count != 0 && seen.Count < 8)
        {
            var batch = candidates.Distinct(StringComparer.Ordinal).Where(address => !seen.Contains(address))
                .Take(8 - seen.Count).ToArray();
            seen.UnionWith(batch);
            candidates.Clear();
            await Task.WhenAll(batch.Select(async address =>
            {
                using var channel = GrpcChannel.ForAddress(address);
                GetLogResponse status;
                try
                {
                    status = await new Storage.StorageClient(channel).GetLogAsync(new GetLogRequest(),
                        deadline: DateTime.UtcNow.AddMilliseconds(500), cancellationToken: token);
                }
                catch (RpcException exception) when (exception.StatusCode is
                    StatusCode.Unavailable or StatusCode.DeadlineExceeded)
                {
                    Console.Error.WriteLine($"Descoberta: {address} indisponivel ({exception.StatusCode}).");
                    return;
                }

                lock (statuses)
                {
                    statuses.Add((address, status));
                    if (TryAddress(status.LeaderAddress, out var leader) && !seen.Contains(leader!.AbsoluteUri))
                    {
                        candidates.Add(leader.AbsoluteUri);
                    }
                }
            }));
        }

        var highestTerm = statuses.Select(status => status.Status.CurrentTerm).DefaultIfEmpty(0).Max();
        var leaders = statuses.Where(status => status.Status.CurrentTerm == highestTerm
            && status.Status.Role is "lider" or "lider fixo").ToArray();
        if (leaders.Length != 1)
        {
            throw new RpcException(new Status(StatusCode.Unavailable,
                "Nao foi possivel identificar um lider unico no termo mais recente; aguarde a eleicao ou configure --peers."));
        }

        Console.WriteLine($"Escrita direcionada para {leaders[0].Address} (termo={highestTerm}).");
        using var leaderChannel = GrpcChannel.ForAddress(leaders[0].Address);
        // Nunca repete a escrita: timeout ou perda da lideranca deixam resultado incerto.
        return await new Storage.StorageClient(leaderChannel).WriteAsync(request,
            deadline: DateTime.UtcNow.Add(timeout), cancellationToken: token);
    }

    public static bool TryAddress(string value, out Uri? address) =>
        Uri.TryCreate(value, UriKind.Absolute, out address)
        && address.Scheme is "http" or "https" && address.AbsolutePath == "/"
        && address.Query == "" && address.Fragment == "" && address.UserInfo == "";
}
