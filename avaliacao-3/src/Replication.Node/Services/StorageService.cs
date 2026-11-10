using Grpc.Core;
using Replication.Contracts;
using Replication.Node.Storage;
using Replication.Node.Replication;

namespace Replication.Node.Services;

public sealed class StorageService(
    JsonKeyValueStore store, FixedNodeOptions options, ILogger<StorageService> logger)
    : Contracts.Storage.StorageBase
{
    public override Task<WriteResponse> Write(WriteRequest request, ServerCallContext context)
    {
        ValidateKey(request.Key);
        context.CancellationToken.ThrowIfCancellationRequested();
        if (!options.IsLeader)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition,
                "Escritas sao aceitas somente pelo lider fixo."),
                new Metadata { { "leader-address", options.LeaderAddress! } });
        }

        try
        {
            var logIndex = store.Put(request.Key, request.Value);
            var status = store.GetStatus();
            logger.LogInformation("No {Node}: lider fixo; termo=0; log={Index}; commit={Commit}; aplicado={Applied}.",
                options.NodeId, status.LastLogIndex, status.CommitIndex, status.LastApplied);
            return Task.FromResult(new WriteResponse
            {
                LogIndex = logIndex,
                CommitIndex = status.CommitIndex,
                LastApplied = status.LastApplied
            });
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
        var snapshot = store.GetLogSnapshot();
        var status = snapshot.Status;
        var response = new GetLogResponse
        {
            LastLogIndex = status.LastLogIndex,
            CommitIndex = status.CommitIndex,
            LastApplied = status.LastApplied,
            NodeId = options.NodeId,
            Role = options.IsLeader ? "lider fixo" : "seguidor",
            LeaderAddress = options.LeaderAddress ?? ""
        };
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

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "A chave nao pode ser vazia."));
        }
    }
}
