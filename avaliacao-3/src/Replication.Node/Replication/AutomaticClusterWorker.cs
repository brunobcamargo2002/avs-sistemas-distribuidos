using Grpc.Core;
using Grpc.Net.Client;
using Replication.Contracts;
using Replication.Node.Storage;

namespace Replication.Node.Replication;

public sealed class AutomaticClusterWorker(
    ElectionState state,
    FixedNodeOptions options,
    JsonKeyValueStore store,
    ILogger<AutomaticClusterWorker> logger) : BackgroundService
{
    private readonly Dictionary<string, string> peerFailures = new(StringComparer.Ordinal);
    private string? divergentLeader;
    private string? replicationLeader;
    private long replicationTerm = -1;
    private long nextLogIndex = 1;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var channels = options.Peers.Where(peer => peer.Key != options.NodeId)
            .ToDictionary(peer => peer.Key, peer => GrpcChannel.ForAddress(peer.Value), StringComparer.Ordinal);
        var clients = channels.ToDictionary(peer => peer.Key,
            peer => new ReplicationTransport.ReplicationTransportClient(peer.Value), StringComparer.Ordinal);
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        string? lastFailure = null;
        try
        {
            do
            {
                string? leader;
                bool timedOut;
                lock (state.SyncRoot)
                {
                    if (state.IsLeader)
                    {
                        continue;
                    }

                    leader = state.LeaderId;
                    timedOut = state.TimedOut;
                }

                if (timedOut)
                {
                    await ElectAsync(clients, stoppingToken);
                }
                else if (leader is null)
                {
                    await DiscoverAsync(clients, stoppingToken);
                }
                else
                {
                    try
                    {
                        await PullAsync(leader, clients[leader], stoppingToken);
                        lastFailure = null;
                    }
                    catch (RpcException exception) when (!stoppingToken.IsCancellationRequested)
                    {
                        // Erro no peer remoto nao derruba este no; o timeout de eleicao trata a falha.
                        var failure = $"{leader}:{exception.StatusCode}";
                        if (lastFailure != failure)
                        {
                            logger.LogWarning("Fetch para {Leader} falhou: {Status} {Detail}. Aguardando timeout de eleicao.",
                                leader, exception.StatusCode, exception.Status.Detail);
                            lastFailure = failure;
                        }
                    }
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Eleicao/replicacao encerrada.");
        }
        catch (RpcException exception) when (stoppingToken.IsCancellationRequested
            && exception.StatusCode == StatusCode.Cancelled)
        {
            logger.LogInformation("RPC cancelado durante encerramento.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidOperationException or ArgumentException)
        {
            logger.LogCritical(exception, "Falha permanente no protocolo ou persistencia; encerrando o host.");
            throw;
        }
        finally
        {
            foreach (var channel in channels.Values)
            {
                channel.Dispose();
            }
        }
    }

    private async Task ElectAsync(
        Dictionary<string, ReplicationTransport.ReplicationTransportClient> clients, CancellationToken token)
    {
        VoteRequest request;
        lock (state.SyncRoot)
        {
            if (state.IsLeader || !state.TimedOut)
            {
                return;
            }

            var log = store.GetLogSnapshot();
            request = new VoteRequest
            {
                Term = state.StartElection(),
                CandidateId = options.NodeId,
                LastLogIndex = log.Status.LastLogIndex,
                LastLogTerm = log.Entries.LastOrDefault()?.Term ?? 0
            };
        }

        var votes = 1;
        await Task.WhenAll(clients.Select(async peer =>
        {
            VoteResponse response;
            try
            {
                response = await peer.Value.RequestVoteAsync(request,
                    deadline: DateTime.UtcNow.AddMilliseconds(350), cancellationToken: token);
            }
            catch (RpcException exception) when (!token.IsCancellationRequested)
            {
                ReportPeerFailure("Pedido de voto", peer.Key, exception);
                return;
            }

            lock (state.SyncRoot)
            {
                state.ObserveTerm(response.Term);
                if (state.Term != request.Term || state.Role != "candidato" || response.Term != request.Term)
                {
                    return;
                }

                if (response.Granted && ++votes >= FixedNodeOptions.Quorum)
                {
                    state.BecomeLeader(request.Term);
                }
            }
        }));
    }

    private void ReportPeerFailure(string operation, string peer, RpcException exception)
    {
        if (exception.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded)
        {
            logger.LogDebug("{Operation} em {Peer} falhou: {Status}.", operation, peer, exception.StatusCode);
            return;
        }

        var failure = $"{exception.StatusCode}:{exception.Status.Detail}";
        lock (peerFailures)
        {
            if (peerFailures.TryGetValue(peer, out var previous) && previous == failure)
            {
                return;
            }

            peerFailures[peer] = failure;
        }

        logger.LogWarning("{Operation} em {Peer} falhou no peer remoto: {Status} {Detail}. O no continua ativo.",
            operation, peer, exception.StatusCode, exception.Status.Detail);
    }

    private async Task DiscoverAsync(
        Dictionary<string, ReplicationTransport.ReplicationTransportClient> clients, CancellationToken token)
    {
        await Task.WhenAll(clients.Select(async peer =>
        {
            LeaderResponse response;
            try
            {
                response = await peer.Value.GetLeaderAsync(new LeaderRequest(),
                    deadline: DateTime.UtcNow.AddMilliseconds(350), cancellationToken: token);
            }
            catch (RpcException exception) when (!token.IsCancellationRequested)
            {
                ReportPeerFailure("Descoberta", peer.Key, exception);
                return;
            }

            lock (state.SyncRoot)
            {
                state.ObserveTerm(response.Term);
                if (!state.IsLeader && response.Term == state.Term && response.Role == "lider"
                    && response.LeaderId == peer.Key)
                {
                    state.SetLeaderHint(peer.Key);
                }
            }
        }));
    }

    private async Task PullAsync(
        string leader, ReplicationTransport.ReplicationTransportClient client, CancellationToken token)
    {
        FetchRequest request;
        long previousIndex;
        lock (state.SyncRoot)
        {
            var log = store.GetLogSnapshot();
            if (replicationLeader != leader || replicationTerm != state.Term)
            {
                replicationLeader = leader;
                replicationTerm = state.Term;
                nextLogIndex = log.Status.LastLogIndex + 1;
            }

            previousIndex = Math.Min(nextLogIndex - 1, log.Status.LastLogIndex);
            request = new FetchRequest
            {
                FollowerId = options.NodeId,
                Term = state.Term,
                LastLogIndex = log.Status.LastLogIndex,
                LastLogTerm = log.Entries.LastOrDefault()?.Term ?? 0,
                PreviousLogIndex = previousIndex,
                PreviousLogTerm = previousIndex == 0 ? 0 : log.Entries[(int)previousIndex - 1].Term
            };
        }

        var response = await client.FetchAsync(request,
            deadline: DateTime.UtcNow.AddMilliseconds(350), cancellationToken: token);
        lock (state.SyncRoot)
        {
            state.ObserveTerm(response.Term);
            if (response.Term != state.Term || request.Term != state.Term || state.IsLeader
                || state.LeaderId != leader)
            {
                return;
            }

            if (!response.Accepted)
            {
                if (response.LogConflict && response.LeaderId == leader)
                {
                    var localCommitIndex = store.GetStatus().CommitIndex;
                    if (previousIndex <= localCommitIndex)
                    {
                        logger.LogError(
                            "O log de {Leader} diverge no indice comitado {CommitIndex}; preservando estado local e rejeitando o lider.",
                            leader, localCommitIndex);
                        state.SetLeaderHint(null);
                        replicationLeader = null;
                        return;
                    }

                    if (response.ConflictIndex < 0 || response.ConflictIndex >= previousIndex)
                    {
                        throw new InvalidOperationException("Indice de conflito invalido na resposta do lider.");
                    }

                    nextLogIndex = Math.Max(1, Math.Min(nextLogIndex - 1, response.ConflictIndex + 1));
                    state.AcceptLeader(response.Term, leader);
                    if (divergentLeader != leader)
                    {
                        logger.LogWarning("Log divergente com {Leader}; negociando o prefixo comum.", leader);
                        divergentLeader = leader;
                    }
                }
                else
                {
                    state.SetLeaderHint(null);
                    replicationLeader = null;
                }

                return;
            }

            if (response.LeaderId != leader || response.LeaderCommit < 0
                || response.Entries.Any(entry => entry.Term > state.Term))
            {
                throw new InvalidOperationException("Resposta de replicacao incompativel com o estado local.");
            }

            var commitIndex = store.GetStatus().CommitIndex;
            if (previousIndex < commitIndex)
            {
                logger.LogError(
                    "O lider {Leader} nao possui o prefixo comitado local ({CommitIndex}); preservando dados e rejeitando o lider.",
                    leader, commitIndex);
                state.SetLeaderHint(null);
                replicationLeader = null;
                return;
            }

            store.ReconcileUncommittedSuffix(previousIndex, response.Entries
                .Select(entry => new JsonKeyValueStore.LogEntrySnapshot(
                    entry.Index, entry.Term, entry.Key, entry.Value, false)).ToArray());
            var lastLogIndex = store.GetStatus().LastLogIndex;
            store.CommitThrough(Math.Min(response.LeaderCommit, lastLogIndex));
            nextLogIndex = previousIndex + response.Entries.Count + 1;
            state.AcceptLeader(response.Term, leader);
            if (divergentLeader is not null)
            {
                logger.LogInformation("Log voltou a ser compativel com {Leader}.", leader);
                divergentLeader = null;
            }
        }
    }
}
