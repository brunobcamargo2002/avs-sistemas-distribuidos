using Grpc.Core;
using Replication.Contracts;
using Replication.Node.Storage;

namespace Replication.Node.Services;

public sealed class StorageService(JsonKeyValueStore store, ILogger<StorageService> logger)
    : Contracts.Storage.StorageBase
{
    public override Task<WriteResponse> Write(WriteRequest request, ServerCallContext context)
    {
        ValidateKey(request.Key);
        context.CancellationToken.ThrowIfCancellationRequested();

        try
        {
            store.Put(request.Key, request.Value);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(exception, "Falha ao persistir uma escrita no armazenamento JSON.");
            throw new RpcException(new Status(StatusCode.Internal, "Nao foi possivel persistir o dado."));
        }

        return Task.FromResult(new WriteResponse());
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

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "A chave nao pode ser vazia."));
        }
    }
}
