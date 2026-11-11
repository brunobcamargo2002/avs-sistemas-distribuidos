using Grpc.Core;
using Replication.Node.Storage;

namespace Replication.Node.Replication;

public sealed class QuorumCoordinator(
    JsonKeyValueStore store,
    FixedNodeOptions options,
    ILogger<QuorumCoordinator> logger,
    TimeProvider clock,
    ElectionState election)
{
    private readonly object gate = election.SyncRoot;
    private readonly Dictionary<string, ReplicaProgress> replicas = new(StringComparer.Ordinal);
    private TaskCompletionSource changed = NewSignal();
    private bool? hadQuorum;
    private bool storageFailed;
    private long leadershipTerm = -1;

    public async Task<long> WriteAsync(string key, string value, CancellationToken cancellationToken)
    {
        long index;
        long term;
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureHealthy();
            EnsureLeader();
            ResetForTerm();
            if (ActiveNodes() < FixedNodeOptions.Quorum)
            {
                logger.LogWarning("Escrita recusada: menos de tres nos ativos.");
                throw NoQuorum();
            }

            term = election.Term;
            index = store.AppendUncommitted(key, value, term);
            logger.LogInformation("Entrada {Index} persistida localmente; aguardando quorum.", index);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(FixedNodeOptions.WriteTimeout);
        try
        {
            while (true)
            {
                Task signal;
                lock (gate)
                {
                    EnsureHealthy();
                    EnsureLeader();
                    if (election.Term != term)
                    {
                        throw new RpcException(new Status(StatusCode.Aborted, "O termo mudou durante a escrita."));
                    }
                    if (store.GetStatus().CommitIndex >= index)
                    {
                        return index;
                    }

                    if (ActiveNodes() < FixedNodeOptions.Quorum)
                    {
                        throw NoQuorum();
                    }

                    signal = changed.Task;
                }

                await signal.WaitAsync(timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new RpcException(new Status(StatusCode.DeadlineExceeded,
                "A escrita nao foi confirmada em 5s; a entrada pode ser comitada posteriormente."));
        }
    }

    public void Confirm(string followerId, long index)
    {
        lock (gate)
        {
            EnsureHealthy();
            EnsureLeader();
            ResetForTerm();
            if (followerId == options.NodeId || !options.Members.Contains(followerId, StringComparer.Ordinal)
                || index < 0 || index > store.GetStatus().LastLogIndex)
            {
                throw new ArgumentException("Confirmacao de replica invalida.");
            }

            replicas[followerId] = new ReplicaProgress(index, clock.GetTimestamp());
            RefreshCore();
        }
    }

    public void Refresh()
    {
        lock (gate)
        {
            RefreshCore();
        }
    }

    public QuorumStatus GetStatus()
    {
        lock (gate)
        {
            ResetForTerm();
            var active = ActiveNodes();
            return new QuorumStatus(active, election.IsLeader && active >= FixedNodeOptions.Quorum && !storageFailed,
                options.Members.Where(member => member != options.NodeId).Select(member =>
                {
                    var known = replicas.TryGetValue(member, out var progress);
                    return new ReplicaStatus(member, known && IsActive(progress!), known ? progress!.Index : 0);
                }).ToArray());
        }
    }

    private void RefreshCore()
    {
        ResetForTerm();
        if (!election.IsLeader)
        {
            Pulse();
            return;
        }

        var active = replicas.Values.Where(IsActive).ToArray();
        var hasQuorum = active.Length + 1 >= FixedNodeOptions.Quorum;
        if (hadQuorum != hasQuorum)
        {
            logger.LogInformation("Quorum {State}: {Active}/4 nos ativos; minimo=3.",
                hasQuorum ? "disponivel" : "indisponivel", active.Length + 1);
            hadQuorum = hasQuorum;
        }

        if (hasQuorum && !storageFailed)
        {
            var index = active.Select(replica => replica.Index)
                .Append(store.GetStatus().LastLogIndex).OrderDescending().ElementAt(FixedNodeOptions.Quorum - 1);
            if (options.AutomaticElection)
            {
                index = store.GetLog().Where(entry => entry.Index <= index && entry.Term == election.Term)
                    .Select(entry => entry.Index).DefaultIfEmpty(0).Max();
            }
            if (index > store.GetStatus().CommitIndex)
            {
                try
                {
                    store.CommitThrough(index);
                    logger.LogInformation("Commit avancou para {Index} apos confirmacao de tres nos.", index);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    storageFailed = true;
                    logger.LogError(exception, "Falha ao persistir commit; novas escritas foram desabilitadas.");
                    Pulse();
                    throw;
                }
            }
        }

        Pulse();
    }

    private int ActiveNodes() => 1 + replicas.Values.Count(IsActive);
    private bool IsActive(ReplicaProgress progress) =>
        clock.GetElapsedTime(progress.LastSeen) <= FixedNodeOptions.HeartbeatTimeout;

    private void EnsureHealthy()
    {
        if (storageFailed)
        {
            throw new RpcException(new Status(StatusCode.Internal, "Falha de persistencia do commit; reinicie o no apos corrigir o armazenamento."));
        }
    }

    private void ResetForTerm()
    {
        if (leadershipTerm == election.Term)
        {
            return;
        }

        replicas.Clear();
        leadershipTerm = election.Term;
        hadQuorum = null;
        Pulse();
    }

    private void EnsureLeader()
    {
        if (!election.IsLeader)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "Este no nao e mais o lider."));
        }
    }

    private void Pulse()
    {
        var previous = changed;
        changed = NewSignal();
        previous.TrySetResult();
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static RpcException NoQuorum() => new(new Status(StatusCode.Unavailable,
        "Sem quorum: sao necessarios tres nos ativos. A escrita nao foi confirmada."));

    private sealed record ReplicaProgress(long Index, long LastSeen);
    public sealed record ReplicaStatus(string NodeId, bool Active, long MatchIndex);
    public sealed record QuorumStatus(int ActiveNodes, bool HasQuorum, IReadOnlyList<ReplicaStatus> Replicas);
}
