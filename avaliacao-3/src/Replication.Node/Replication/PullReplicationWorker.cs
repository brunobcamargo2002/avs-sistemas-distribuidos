using Grpc.Core;
using Grpc.Net.Client;
using Replication.Contracts;
using Replication.Node.Storage;

namespace Replication.Node.Replication;

public sealed class PullReplicationWorker(
    JsonKeyValueStore store,
    FixedNodeOptions options,
    ILogger<PullReplicationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // A configuracao e fixa; nao ha descoberta ou eleicao nesta etapa.
        using var channel = GrpcChannel.ForAddress(options.LeaderAddress!);
        var client = new ReplicationTransport.ReplicationTransportClient(channel);
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        var disconnected = false;

        try
        {
            store.EnsureLogBackedState();
            do
            {
                var before = store.GetLogSnapshot();
                FetchResponse response;
                try
                {
                    response = await client.FetchAsync(new FetchRequest
                    {
                        FollowerId = options.NodeId,
                        LastLogIndex = before.Status.LastLogIndex,
                        LastLogTerm = before.Entries.LastOrDefault()?.Term ?? 0
                    }, deadline: DateTime.UtcNow.AddSeconds(2), cancellationToken: stoppingToken);
                }
                catch (RpcException exception) when (exception.StatusCode is
                    StatusCode.Unavailable or StatusCode.DeadlineExceeded)
                {
                    if (!disconnected)
                    {
                        logger.LogWarning("Lider {Leader} indisponivel ({Status}); novas tentativas serao feitas.",
                            options.LeaderAddress, exception.StatusCode);
                        disconnected = true;
                    }

                    continue;
                }

                if (response.LeaderCommit < before.Status.CommitIndex
                    || string.IsNullOrWhiteSpace(response.LeaderId)
                    || response.LeaderId == options.NodeId)
                {
                    throw new InvalidOperationException("O lider retornou estado de replicacao incompativel.");
                }

                store.AppendReplicated(response.Entries.Select(entry =>
                    new JsonKeyValueStore.LogEntrySnapshot(
                        entry.Index, entry.Term, entry.Key, entry.Value, false)).ToArray());

                // So aplica o prefixo ja persistido e confirmado neste Fetch, nao as entradas recem-recebidas.
                store.CommitThrough(Math.Min(response.LeaderCommit, before.Status.LastLogIndex));
                if (disconnected)
                {
                    logger.LogInformation("Comunicacao com o lider {Leader} restabelecida.", response.LeaderId);
                    disconnected = false;
                }

                var after = store.GetStatus();
                if (after != before.Status)
                {
                    logger.LogInformation("No {Node}: seguidor; termo={Term}; log={Index}; commit={Commit}; aplicado={Applied}.",
                        options.NodeId, store.GetLog().LastOrDefault()?.Term ?? 0,
                        after.LastLogIndex, after.CommitIndex, after.LastApplied);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Replicacao Pull encerrada.");
        }
        catch (RpcException exception) when (stoppingToken.IsCancellationRequested
            && exception.StatusCode == StatusCode.Cancelled)
        {
            logger.LogInformation("Fetch cancelado durante encerramento.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidOperationException or ArgumentException or RpcException)
        {
            logger.LogCritical(exception, "Replicacao interrompida; o host sera encerrado para nao ocultar uma falha permanente.");
            throw;
        }
    }
}
