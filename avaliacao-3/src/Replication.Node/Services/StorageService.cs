using Grpc.Core;
using Replication.Contracts;
using Replication.Node.Storage;
using Replication.Node.Replication;

namespace Replication.Node.Services;

public sealed class StorageService(
    JsonKeyValueStore store, FixedNodeOptions options, ElectionState election,
    QuorumCoordinator quorum, ILogger<StorageService> logger)
    : Contracts.Storage.StorageBase
{
    public override async Task<WriteResponse> Write(WriteRequest request, ServerCallContext context)
    {
        ValidateKey(request.Key);
        context.CancellationToken.ThrowIfCancellationRequested();
        lock (election.SyncRoot)
        {
            if (!election.IsLeader)
            {
                var metadata = new Metadata();
                if (election.LeaderAddress is { } address)
                {
                    metadata.Add("leader-address", address);
                }

                throw new RpcException(new Status(StatusCode.FailedPrecondition,
                    election.LeaderAddress is null ? "Lider ainda desconhecido; eleicao em andamento."
                        : "Escritas sao aceitas somente pelo lider."), metadata);
            }
        }

        try
        {
            var logIndex = await quorum.WriteAsync(request.Key, request.Value, context.CancellationToken);
            var status = store.GetStatus();
            logger.LogInformation("No {Node}: escrita confirmada; log={Index}; commit={Commit}; aplicado={Applied}.",
                options.NodeId, status.LastLogIndex, status.CommitIndex, status.LastApplied);
            return new WriteResponse
            {
                LogIndex = logIndex,
                CommitIndex = status.CommitIndex,
                LastApplied = status.LastApplied
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(exception, "Falha ao persistir uma escrita no armazenamento JSON.");
            throw new RpcException(new Status(StatusCode.Internal, "Nao foi possivel persistir o dado."));
        }
    }

    public override Task<ReadResponse> Read(ReadRequest request, ServerCallContext context)
    {
        ValidateKey(request.Key);
        context.CancellationToken.ThrowIfCancellationRequested();
        if (!store.TryGet(request.Key, out var value))
        {
            throw new RpcException(new Status(StatusCode.NotFound, "Chave nao encontrada."));
        }

        return Task.FromResult(new ReadResponse { Value = value });
    }

    public override Task<ListResponse> List(ListRequest request, ServerCallContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        var response = new ListResponse();
        response.Entries.AddRange(store.GetAll()
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new Entry { Key = entry.Key, Value = entry.Value }));
        return Task.FromResult(response);
    }

    public override Task<GetLogResponse> GetLog(GetLogRequest request, ServerCallContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        lock (election.SyncRoot)
        {
            var snapshot = store.GetLogSnapshot();
            var status = snapshot.Status;
            var quorumStatus = quorum.GetStatus();
            var response = new GetLogResponse
            {
                LastLogIndex = status.LastLogIndex,
                CommitIndex = status.CommitIndex,
                LastApplied = status.LastApplied,
                NodeId = options.NodeId,
                Role = election.Role,
                CurrentTerm = election.Term,
                LeaderAddress = election.LeaderAddress ?? "",
                QuorumSize = FixedNodeOptions.Quorum,
                ActiveNodes = election.IsLeader ? quorumStatus.ActiveNodes : 0,
                HasQuorum = election.IsLeader && quorumStatus.HasQuorum
            };
            if (election.IsLeader)
            {
                response.Replicas.AddRange(quorumStatus.Replicas.Select(replica => new ReplicaProgress
                {
                    NodeId = replica.NodeId,
                    Active = replica.Active,
                    MatchIndex = replica.MatchIndex
                }));
            }
            response.Entries.AddRange(snapshot.Entries.Select(entry => new LogEntry
            {
                Index = entry.Index,
                Term = entry.Term,
                Key = entry.Key,
                Value = entry.Value,
                IsCommitted = entry.IsCommitted
            }));
            return Task.FromResult(response);
        }
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "A chave nao pode ser vazia."));
        }
    }
}
