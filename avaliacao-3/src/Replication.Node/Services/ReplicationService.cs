using Grpc.Core;
using Replication.Contracts;
using Replication.Node.Replication;
using Replication.Node.Storage;

namespace Replication.Node.Services;

public sealed class ReplicationService(
    JsonKeyValueStore store,
    FixedNodeOptions options,
    ILogger<ReplicationService> logger) : ReplicationTransport.ReplicationTransportBase
{
    public override Task<FetchResponse> Fetch(FetchRequest request, ServerCallContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (!options.IsLeader)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "Este no nao e o lider fixo."));
        }

        if (string.IsNullOrWhiteSpace(request.FollowerId) || request.FollowerId == options.NodeId
            || request.LastLogIndex < 0 || request.LastLogTerm < 0
            || (request.LastLogIndex == 0 && request.LastLogTerm != 0))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Identidade ou posicao do seguidor invalida."));
        }

        try
        {
            store.EnsureLogBackedState();
        }
        catch (InvalidOperationException exception)
        {
            logger.LogError(exception, "Estado do lider incompativel com replicacao por log.");
            throw new RpcException(new Status(StatusCode.FailedPrecondition, exception.Message));
        }

        var snapshot = store.GetLogSnapshot();
        if (request.LastLogIndex > snapshot.Status.LastLogIndex
            || (request.LastLogIndex > 0
                && snapshot.Entries[(int)request.LastLogIndex - 1].Term != request.LastLogTerm))
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition,
                "Log divergente. Truncamento e troca de lider ainda nao estao implementados nesta etapa."));
        }

        logger.LogDebug("Fetch de {Follower}: armazenamento confirmado ate {Index}.",
            request.FollowerId, request.LastLogIndex);
        var response = new FetchResponse
        {
            LeaderId = options.NodeId,
            LeaderCommit = snapshot.Status.CommitIndex
        };
        response.Entries.AddRange(snapshot.Entries.Skip((int)request.LastLogIndex).Take(128)
            .Select(entry => new LogEntry
            {
                Index = entry.Index,
                Term = entry.Term,
                Key = entry.Key,
                Value = entry.Value
            }));
        return Task.FromResult(response);
    }
}
